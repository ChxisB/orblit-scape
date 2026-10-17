import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

import 'engine/filtered_interest.dart';
import 'engine/phase.dart';
import 'engine/registry.dart';
import 'engine/services.dart';
import 'engine/summary_text.dart';
import 'engine/tick_loop.dart';
import 'features.dart';
import 'features/interactions/interactions.dart';
import 'features/items/ground.dart';
import 'features/items/items.dart';
import 'features/combat/combat.dart';
import 'features/movement/movement.dart';
import 'features/npcs/npcs.dart';
import 'features/objects/objects.dart';
import 'features/prayer/prayers.dart';
import 'features/quests/quests.dart';
import 'features/skills/skills.dart';
import 'features/social/social.dart';
import 'accounts/accounts.dart';
import 'entrance.dart';
import 'inbox.dart';
import 'persistence/player_states.dart';
import 'persistence/saves.dart';
import 'persistence/storage.dart';
import 'wire.dart';

/// The bytes the net layer adds round a message before the host counts it.
const int _wrapperBytes = 4;

/// Ticks a connection may stay silent before it must log in, 12 seconds.
const int _loginTicks = 20;

/// Ticks a dropped player's avatar stays in the world, 6 seconds.
const int _lingerTicks = 10;

/// Ticks a dropped player who is in a fight stays, 60 seconds, so pulling
/// the plug is not a way out of one.
const int _fightingLingerTicks = 100;

/// Interest and active cells share one grid of 16 tiles a side.
const int _cellTiles = 16;

/// A player is told about the cell they stand in and the ring round it.
const int _interestRing = 1;

/// A cell within this many cells of a player is simulated.
const int _activeRadius = 1;

/// Ticks a cell stays simulated once no player is near it.
const int _activeHoldTicks = 10;

/// What the wire and the features reach the host through. The host is built
/// after the features register, so it arrives late.
final class _HostBridge implements WireLink, Hosting {
  late final NetHost host;

  @override
  bool isLive(String key) => host.sessionState(key) == SessionState.live;

  @override
  void send(String key, Uint8List body) => host.sendMessage(key, body);

  @override
  int spawn(int entity, {String? owner}) => host.spawn(entity, owner: owner);

  @override
  void despawn(int entity) => host.despawn(entity);

  @override
  void tell(String player, ServerMessage message) =>
      send(player, encodeServerMessage(message));
}

/// The world, the host and the loop, wired together.
final class ScapeServer {
  ScapeServer({
    required TickClock clock,
    required Pacer pacer,
    required void Function(String line) log,
    required TileMap map,
    required TileCoord spawn,
    required SkillSet skills,
    required ItemSet items,
    required ObjectSet objects,
    required NodeSet nodes,
    required NpcSet npcs,
    required CombatRules combat,
    RecipeSet? recipes,
    ShopRules? shops,
    Story? story,
    SpellSet? spells,
    PrayerSet? prayers,
    Storage? storage,
    Random? random,
  }) {
    final networkId = world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    final bridge = _HostBridge();
    final grid = Grid(_cellTiles.toDouble());
    active = ActiveCells(
      grid: grid,
      radius: _activeRadius,
      hold: tickPeriod * _activeHoldTicks,
      clock: () => tickPeriod * registry.actions.tick,
    );
    registry.addSystem(Phase.inbound, _deliverIntents);
    final features = registerFeatures(
      registry,
      Services(world: world, map: map, hosting: bridge, active: active),
      spawn: spawn,
      skills: skills,
      items: items,
      objects: objects,
      nodes: nodes,
      npcs: npcs,
      combat: combat,
      recipes: recipes,
      shops: shops,
      story: story,
      spells: spells,
      prayers: prayers,
      random: random ?? Random(),
      log: log,
      settled: (a, b) => _saves?.saveTogether(a, b),
    );
    social = features.social;
    this.skills = features.skills;
    this.items = features.items;
    this.npcs = features.npcs;
    this.combat = features.combat;
    movement = features.movement;
    ground = features.ground;
    this.objects = features.objects;
    interactions = features.interactions;
    quests = features.quests;
    this.prayers = features.prayers;
    final saves = storage == null
        ? null
        : Saves(
            queue: storage.queue,
            states: PlayerStates(
              movement: features.movement,
              skills: features.skills,
              items: features.items,
              social: features.social,
              combat: features.combat,
              quests: features.quests,
              map: map,
            ),
            autosave: storage.autosave,
            hasSession: (player) => bridge.host.sessionState(player) != null,
            log: log,
          );
    _saves = saves;
    if (saves != null) {
      registry.addSystem(
        Phase.outbound,
        () => saves.autosave(registry.actions.tick),
      );
    }
    wire = Wire(
      link: bridge,
      inbox: inbox,
      log: log,
      gate: saves == null ? null : _entranceOf(storage!, saves).check,
    );
    final set = ReplicationSet(registry.replicated);
    final nearby = GridInterest(
      set: set,
      position: registry.replicated.firstWhere(
        (c) => c.name == tilePositionName,
      ),
      grid: grid,
      ring: _interestRing,
    );
    final interest = FilteredInterest(nearby, ground.canSee);
    registry.addSystem(Phase.inbound, () => active.update(nearby.focusCells));
    host = NetHost(
      world: world,
      set: set,
      networkId: networkId,
      clock: clock,
      interest: interest,
      onOverrun: (summary) => log(describeLateTick(summary)),
      sessions: SessionPolicy(
        admit: wire.admit,
        onJoin: (key) {
          wire.onJoin(key);
          registry.playerJoined(key);
          saves?.arrived(key);
        },
        onGone: (key, reason) {
          saves?.left(key, reason);
          wire.onGone(key, reason);
          registry.playerGone(key);
        },
        joinTicks: _loginTicks,
        lingerTicks: _lingerTicks,
        lingerFor: (key) => features.combat.isFighting(key)
            ? _fightingLingerTicks
            : _lingerTicks,
      ),
      budget: InboundBudget(
        messagesPerTick: 20,
        bytesPerMessage: clientMessageLimit + _wrapperBytes,
        bytesPerTick: 4096,
      ),
      onMessage: wire.onMessage,
    );
    bridge.host = host;
    loop = TickLoop(host: host, registry: registry, pacer: pacer);
  }

  final World world = World();
  final Registry registry = Registry();
  final Inbox inbox = Inbox();
  late final ActiveCells active;
  late final Movement movement;
  late final Social social;
  late final Skills skills;
  late final Items items;
  late final Npcs npcs;
  late final Combat combat;
  late final GroundItems ground;
  late final WorldObjects objects;
  late final Interactions interactions;
  late final Quests? quests;
  late final Prayers? prayers;
  late final Wire wire;
  late final Saves? _saves;
  late final NetHost host;
  late final TickLoop loop;

  /// Accepts WebSocket connections on [address] and [port]. A port of zero
  /// lets the system choose, and the result says which. The caller closes it.
  Future<SocketServer> listen({
    Object address = '127.0.0.1',
    int port = 0,
  }) async {
    final sockets = await SocketServer.bind(address: address, port: port);
    sockets.connections.listen(host.accept);
    return sockets;
  }

  void _deliverIntents() {
    for (final (:player, :intent) in inbox.drain()) {
      registry.deliver(player, intent);
    }
  }

  /// Saves every player in the world, waits for the store, and stops the host.
  Future<void> dispose() async {
    await _saves?.shutdown();
    await host.dispose();
    world.dispose();
  }
}

Entrance _entranceOf(Storage storage, Saves saves) => Entrance(
  saves: saves,
  accounts: switch (storage.hasher) {
    final hasher? => Accounts(
      queue: storage.queue,
      hasher: hasher,
      random: storage.salt,
    ),
    null => null,
  },
);
