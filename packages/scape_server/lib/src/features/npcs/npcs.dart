import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';

/// The tile each player in the world stands on.
typedef PlayerTiles = Iterable<TileCoord> Function();

/// The chance each tick that a creature with nothing to do sets off.
const double wanderChance = 0.1;

/// A creature standing in the world.
final class PlacedNpc {
  PlacedNpc._({
    required this.entity,
    required this.networkId,
    required this.def,
    required this.home,
    required this.radius,
  }) : leash = switch (def.behaviour) {
         Aggressive(:final leash) => max(leash, radius),
         Idle() || Wander() => radius,
       },
       _mover = Mover(home, walker: Walker.npc);

  final int entity;
  final int networkId;
  final NpcDef def;

  /// Where it was placed, and where it goes back to.
  final TileCoord home;

  /// How far from [home] it wanders.
  final int radius;

  /// How far from [home] it ever goes.
  final int leash;

  final Mover _mover;
  bool _asleep = false;
  bool _hunting = false;

  /// The tile it stands on.
  TileCoord get tile => _mover.position;
}

/// The creatures of the world. They walk on the NPC phase, and only while a
/// player is near enough to make their home cell active. A creature whose
/// cell was asleep is put back at its home tile when the cell wakes, so a
/// region nobody visited never holds a creature mid walk.
final class Npcs {
  Npcs._(
    this._services,
    this._defs,
    this._playerTiles,
    this._random,
    this._position,
    this._npc,
  );

  /// Adds creatures to [registry] and returns the feature, so a feature that
  /// places creatures can spawn them.
  factory Npcs.register(
    Registry registry,
    Services services, {
    required NpcSet npcs,
    required PlayerTiles playerTiles,
    required Random random,
  }) {
    final world = services.world;
    final npc = world.registerComponent(
      npcName,
      kind: ComponentKind.int32,
      arity: npcInts,
    );
    registry.replicate(npc);
    final feature = Npcs._(
      services,
      npcs,
      playerTiles,
      random,
      world.registerComponent(
        tilePositionName,
        kind: ComponentKind.float32,
        arity: tilePositionFloats,
      ),
      npc,
    );
    registry.addSystem(Phase.npcs, feature._step);
    return feature;
  }

  final Services _services;
  final NpcSet _defs;
  final PlayerTiles _playerTiles;
  final Random _random;
  final ComponentType _position;
  final ComponentType _npc;
  final List<PlacedNpc> _all = [];

  /// How many creatures are in the world.
  int get count => _all.length;

  /// Puts a creature in the world and returns it. Throws [ArgumentError] for
  /// a creature that does not exist and for a tile a creature may not stand
  /// on.
  // F5: the returned creature is how a caller finds what it just placed.
  PlacedNpc spawn(NpcSpawn spawn) {
    final def = _defs.require(spawn.npc);
    final barred = TileFlags.blocked | TileFlags.water | TileFlags.noNpc;
    if (_services.map.flagsAt(spawn.tile).has(barred)) {
      throw ArgumentError.value(spawn.tile, 'tile', 'is not open to creatures');
    }
    final world = _services.world;
    final entity = world.createEntity();
    world
      ..add(entity, _position)
      ..add(entity, _npc);
    ByteData.sublistView(
      world.bytesOf(entity, _npc)!,
    ).setInt32(0, def.id, Endian.little);
    final placed = PlacedNpc._(
      entity: entity,
      networkId: _services.hosting.spawn(entity),
      def: def,
      home: spawn.tile,
      radius: spawn.radius,
    );
    _write(placed);
    _all.add(placed);
    return placed;
  }

  void _step() {
    final players = _playerTiles().toList();
    for (final npc in _all) {
      if (!_isActive(npc)) {
        npc._asleep = true;
      } else if (npc._asleep) {
        _wake(npc);
      } else {
        _move(npc, players);
      }
    }
  }

  bool _isActive(PlacedNpc npc) =>
      _services.active.isActiveAt(npc.home.x.toDouble(), npc.home.y.toDouble());

  void _wake(PlacedNpc npc) {
    npc
      .._asleep = false
      .._mover.clearPath()
      .._mover.position = npc.home;
    _write(npc);
  }

  void _move(PlacedNpc npc, List<TileCoord> players) {
    _decide(npc, players);
    npc._mover.advance(_services.map);
    if (npc._mover.stepsLastTick.isNotEmpty) _write(npc);
  }

  void _decide(PlacedNpc npc, List<TileCoord> players) {
    final prey = _preyOf(npc, players);
    if (prey != null) {
      _chase(npc, prey);
    } else if (npc._hunting) {
      npc._hunting = false;
      _walkTo(npc, npc.home);
    } else if (!npc._mover.isMoving) {
      _roam(npc);
    }
  }

  /// The nearest player this creature would chase, or null.
  TileCoord? _preyOf(PlacedNpc npc, List<TileCoord> players) =>
      switch (npc.def.behaviour) {
        // A creature notices at its sight and keeps on to its leash.
        Aggressive(:final sight, :final leash) => _nearest(
          npc,
          players,
          npc._hunting ? leash : sight,
          leash,
        ),
        Idle() || Wander() => null,
      };

  TileCoord? _nearest(
    PlacedNpc npc,
    List<TileCoord> players,
    int sight,
    int leash,
  ) {
    TileCoord? nearest;
    for (final player in players) {
      final seen =
          player.plane == npc.home.plane &&
          _gap(npc.tile, player) <= sight &&
          _gap(npc.home, player) <= leash;
      if (seen &&
          (nearest == null ||
              _gap(npc.tile, player) < _gap(npc.tile, nearest))) {
        nearest = player;
      }
    }
    return nearest;
  }

  void _chase(PlacedNpc npc, TileCoord prey) {
    npc._hunting = true;
    final reach = Beside(Footprint.single(prey));
    if (reachSatisfied(_services.map, reach, npc.tile)) {
      npc._mover.clearPath();
      return;
    }
    final path = findPathToReach(
      _services.map,
      npc.tile,
      reach,
      walker: Walker.npc,
    );
    npc._mover.setPath(_inside(npc, path.steps));
  }

  void _roam(PlacedNpc npc) {
    if (_gap(npc.tile, npc.home) > npc.radius) {
      _walkTo(npc, npc.home);
    } else if (npc.def.behaviour is! Idle &&
        _random.nextDouble() < wanderChance) {
      _walkTo(npc, _aroundHome(npc));
    }
  }

  TileCoord _aroundHome(PlacedNpc npc) {
    int offset() =>
        (_random.nextDouble() * (2 * npc.radius + 1)).floor() - npc.radius;
    return TileCoord(
      (npc.home.x + offset()).clamp(0, worldSize - 1),
      (npc.home.y + offset()).clamp(0, worldSize - 1),
      npc.home.plane,
    );
  }

  void _walkTo(PlacedNpc npc, TileCoord target) {
    final path = findPath(_services.map, npc.tile, target, walker: Walker.npc);
    npc._mover.setPath(_inside(npc, path.steps));
  }

  /// The steps of [steps] up to the first that leaves the leash.
  List<TileCoord> _inside(PlacedNpc npc, List<TileCoord> steps) =>
      steps.takeWhile((step) => _gap(npc.home, step) <= npc.leash).toList();

  /// The distance in tiles, counting a diagonal as one.
  int _gap(TileCoord a, TileCoord b) =>
      max((a.x - b.x).abs(), (a.y - b.y).abs());

  void _write(PlacedNpc npc) {
    writeTilePosition(
      _services.world.bytesOf(npc.entity, _position)!,
      npc.tile,
    );
  }
}
