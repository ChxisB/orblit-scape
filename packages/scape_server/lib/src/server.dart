import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

import 'engine/phase.dart';
import 'engine/registry.dart';
import 'engine/services.dart';
import 'engine/summary_text.dart';
import 'engine/tick_loop.dart';
import 'features.dart';
import 'features/social/social.dart';
import 'inbox.dart';
import 'wire.dart';

/// The bytes the net layer adds round a message before the host counts it.
const int _wrapperBytes = 4;

/// Ticks a connection may stay silent before it must log in, 12 seconds.
const int _loginTicks = 20;

/// Ticks a dropped player's avatar stays in the world, 6 seconds.
const int _lingerTicks = 10;

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
  }) {
    final networkId = world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    final bridge = _HostBridge();
    wire = Wire(link: bridge, inbox: inbox);
    final grid = Grid(_cellTiles.toDouble());
    active = ActiveCells(
      grid: grid,
      radius: _activeRadius,
      hold: tickPeriod * _activeHoldTicks,
      clock: () => tickPeriod * registry.actions.tick,
    );
    registry.addSystem(Phase.inbound, _deliverIntents);
    social = registerFeatures(
      registry,
      Services(world: world, map: map, hosting: bridge, active: active),
      spawn: spawn,
    ).social;
    final set = ReplicationSet(registry.replicated);
    final interest = GridInterest(
      set: set,
      position: registry.replicated.firstWhere(
        (c) => c.name == tilePositionName,
      ),
      grid: grid,
      ring: _interestRing,
    );
    registry.addSystem(Phase.inbound, () => active.update(interest.focusCells));
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
        },
        onGone: (key, reason) {
          wire.onGone(key, reason);
          registry.playerGone(key);
        },
        joinTicks: _loginTicks,
        lingerTicks: _lingerTicks,
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
  late final Social social;
  late final Wire wire;
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

  Future<void> dispose() async {
    await host.dispose();
    world.dispose();
  }
}
