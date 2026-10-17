import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/action.dart';
import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';

final class _Player {
  _Player(this.entity, this.networkId, this.mover);

  final int entity;
  final int networkId;
  final Mover mover;
}

/// Players that walk. A walk intent finds a path on the action tick, and the
/// movement phase takes the steps and writes where each player stands.
final class Movement {
  Movement._(this._registry, this._services, this._spawn, this._position);

  /// Adds walking to [registry] and returns the feature, so a test or a later
  /// feature can ask where a player stands.
  factory Movement.register(
    Registry registry,
    Services services, {
    required TileCoord spawn,
  }) {
    if (services.map.flagsAt(spawn).has(TileFlags.blocked | TileFlags.water)) {
      throw ArgumentError.value(spawn, 'spawn', 'is not open ground');
    }
    final position = services.world.registerComponent(
      tilePositionName,
      kind: ComponentKind.float32,
      arity: tilePositionFloats,
    );
    registry.replicate(position);
    final movement = Movement._(registry, services, spawn, position);
    registry
      ..addKind(movement._walk)
      ..addJoinHandler(movement._join)
      ..addGoneHandler(movement._gone)
      ..addIntentHandler(movement._onIntent)
      ..addSystem(Phase.movement, movement._step);
    return movement;
  }

  final Registry _registry;
  final Services _services;
  final TileCoord _spawn;
  final ComponentType _position;
  final Map<String, _Player> _players = {};
  final ActionKind _walk = ActionKind('walk', cancels: const CancelsAll());

  /// The tile [player] stands on, or null when they are not in the world.
  TileCoord? tileOf(String player) => _players[player]?.mover.position;

  /// The key actions of [player] are queued under, or null when they are not
  /// in the world.
  int? actorOf(String player) => _players[player]?.entity;

  /// The key of each player in the world.
  Iterable<String> get players => _players.keys;

  /// The tile each player in the world stands on.
  Iterable<TileCoord> get tiles => [
    for (final player in _players.values) player.mover.position,
  ];

  bool isMoving(String player) => _players[player]?.mover.isMoving ?? false;

  /// Sets [player] walking along [steps], each beside the one before, or
  /// stops them when it is empty.
  void follow(String player, List<TileCoord> steps) =>
      _players[player]?.mover.setPath(steps);

  /// Puts [player] on [tile] with no walk between, such as up a stair.
  void teleport(String player, TileCoord tile) {
    final found = _players[player];
    if (found == null) return;
    found.mover
      ..clearPath()
      ..position = tile;
    _write(found.entity, tile);
  }

  /// The replicated entity and the tile of [player], or null when they are not
  /// in the world.
  ({int networkId, TileCoord tile})? standing(String player) {
    final found = _players[player];
    if (found == null) return null;
    return (networkId: found.networkId, tile: found.mover.position);
  }

  void _join(String key) {
    final world = _services.world;
    final entity = world.createEntity();
    world.add(entity, _position);
    final networkId = _services.hosting.spawn(entity, owner: key);
    _players[key] = _Player(entity, networkId, Mover(_spawn));
    _write(entity, _spawn);
    _services.hosting.tell(key, Avatar(networkId));
  }

  void _gone(String key) {
    final player = _players.remove(key);
    if (player != null) _services.hosting.despawn(player.entity);
  }

  void _onIntent(String key, Intent intent) {
    final player = _players[key];
    if (player == null || intent is! Walk) return;
    _registry.actions.submit(
      player.entity,
      _walk,
      () => _startWalk(player, intent.target),
    );
  }

  void _startWalk(_Player player, TileCoord target) {
    final from = player.mover.position;
    if (target.plane != from.plane) return;
    player.mover.setPath(findPath(_services.map, from, target).steps);
  }

  void _step() {
    for (final player in _players.values) {
      player.mover.advance(_services.map);
      if (player.mover.stepsLastTick.isNotEmpty) {
        _write(player.entity, player.mover.position);
      }
    }
  }

  void _write(int entity, TileCoord tile) {
    writeTilePosition(_services.world.bytesOf(entity, _position)!, tile);
  }
}
