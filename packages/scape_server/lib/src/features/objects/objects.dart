import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';

/// An object standing in the world.
final class PlacedObject {
  PlacedObject._({
    required this.entity,
    required this.networkId,
    required this.def,
    required this.footprint,
    required this.destination,
  });

  final int entity;
  final int networkId;
  final ObjectDef def;
  final Footprint footprint;

  /// Where climbing it leads, for an object that has such a way.
  final TileCoord? destination;

  int _state = 0;

  int get state => _state;
}

/// The objects of the world: trees, doors, stairs. Each is an entity the
/// clients near it draw. A state change updates the entity and the flags of
/// its tiles together, so a door that opens can be walked through.
final class WorldObjects {
  WorldObjects._(this._services, this._objects, this._position, this._object);

  /// Adds objects to [registry] and returns the feature, so a feature that
  /// owns a kind of object can place it and change its state.
  factory WorldObjects.register(
    Registry registry,
    Services services, {
    required ObjectSet objects,
  }) {
    final world = services.world;
    final object = world.registerComponent(
      worldObjectName,
      kind: ComponentKind.int32,
      arity: worldObjectInts,
    );
    registry.replicate(object);
    return WorldObjects._(
      services,
      objects,
      world.registerComponent(
        tilePositionName,
        kind: ComponentKind.float32,
        arity: tilePositionFloats,
      ),
      object,
    );
  }

  final Services _services;
  final ObjectSet _objects;
  final ComponentType _position;
  final ComponentType _object;
  final Map<int, PlacedObject> _placed = {};
  final Map<TileCoord, PlacedObject> _byTile = {};

  /// The object with [networkId], or null when there is none.
  PlacedObject? find(int networkId) => _placed[networkId];

  /// The object standing on [tile], or null.
  PlacedObject? at(TileCoord tile) => _byTile[tile];

  /// Stands the object [id] with its south-west tile on [origin] and returns
  /// it. Throws [ArgumentError] for an object that does not exist and for a
  /// tile that is blocked, water or taken.
  // F5: the returned object is how a caller finds what it just placed.
  PlacedObject place(int id, TileCoord origin, {TileCoord? destination}) {
    final def = _objects.require(id);
    final footprint = def.footprintAt(origin);
    _checkFree(footprint);
    final world = _services.world;
    final entity = world.createEntity();
    world
      ..add(entity, _position)
      ..add(entity, _object);
    writeTilePosition(world.bytesOf(entity, _position)!, origin);
    final placed = PlacedObject._(
      entity: entity,
      networkId: _services.hosting.spawn(entity),
      def: def,
      footprint: footprint,
      destination: destination,
    );
    _placed[placed.networkId] = placed;
    for (final tile in footprint.tiles) {
      _byTile[tile] = placed;
    }
    _write(placed);
    _setBlocking(placed);
    return placed;
  }

  /// Puts [object] in [state], which its clients are told and which changes
  /// whether its tiles block.
  void setState(PlacedObject object, int state) {
    if (state < 0 || state > 0x7FFFFFFF) {
      throw RangeError.value(state, 'state', 'does not fit an int32');
    }
    object._state = state;
    _write(object);
    _setBlocking(object);
  }

  void _checkFree(Footprint footprint) {
    for (final tile in footprint.tiles) {
      final flags = _services.map.flagsAt(tile);
      if (flags.has(TileFlags.blocked | TileFlags.water) ||
          _byTile[tile] != null) {
        throw ArgumentError.value(tile, 'origin', 'is not free ground');
      }
    }
  }

  void _write(PlacedObject object) {
    ByteData.sublistView(_services.world.bytesOf(object.entity, _object)!)
      ..setInt32(0, object.def.id, Endian.little)
      ..setInt32(4, object.state, Endian.little);
  }

  void _setBlocking(PlacedObject object) {
    final map = _services.map;
    final blocks = object.def.blocksIn(object.state);
    for (final tile in object.footprint.tiles) {
      final flags = map.flagsAt(tile);
      map.setFlags(
        tile,
        blocks ? flags | TileFlags.blocked : flags.without(TileFlags.blocked),
      );
    }
  }
}
