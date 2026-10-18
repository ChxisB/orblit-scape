import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';

import 'npcs.dart';
import 'objects.dart';

/// The ground and everything standing on it: the flags of every region, where
/// a new player appears, the objects and the creatures.
final class WorldPlan {
  WorldPlan({
    required this.map,
    required this.spawn,
    required List<ObjectPlacement> objects,
    required List<NpcSpawn> npcs,
  }) : objects = List.unmodifiable(objects),
       npcs = List.unmodifiable(npcs);

  /// Mutable, because the server flags the tiles of a solid object blocked.
  final TileMap map;
  final TileCoord spawn;
  final List<ObjectPlacement> objects;
  final List<NpcSpawn> npcs;
}

// Land is an ellipse across the four regions of plane 0.
const int _across = 2;
const int _centre = _across * regionSize ~/ 2;
const int _radiusX = 54;
const int _radiusY = 50;

TileCoord _at(int x, int y, [int plane = 0]) => TileCoord(x, y, plane);

bool _isLand(int x, int y) {
  final dx = (x - _centre) / _radiusX;
  final dy = (y - _centre) / _radiusY;
  return dx * dx + dy * dy <= 1;
}

void _flag(TileMap map, TileCoord tile, TileFlags flags) =>
    map.setFlags(tile, map.flagsAt(tile) | flags);

/// A room whose floor is the tiles from [from] to [to], as (x, y). Walls
/// stand on its outer edges, with a gap in the south wall at [doorX].
void _room(
  TileMap map, {
  required (int, int) from,
  required (int, int) to,
  required int doorX,
}) {
  final (x0, y0) = from;
  final (x1, y1) = to;
  for (var y = y0; y <= y1; y++) {
    for (var x = x0; x <= x1; x++) {
      var walls = TileFlags.open;
      if (y == y0 && x != doorX) walls |= TileFlags.wallSouth;
      if (y == y1) walls |= TileFlags.wallNorth;
      if (x == x0) walls |= TileFlags.wallWest;
      if (x == x1) walls |= TileFlags.wallEast;
      _flag(map, _at(x, y), walls);
    }
  }
}

/// Ground and sea on plane 0, rooms on it, and the floor above the tower.
TileMap _terrain() {
  final map = TileMap();
  for (var y = 0; y < _across; y++) {
    for (var x = 0; x < _across; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  for (var y = 0; y < _across * regionSize; y++) {
    for (var x = 0; x < _across * regionSize; x++) {
      if (!_isLand(x, y)) _flag(map, _at(x, y), TileFlags.water);
    }
  }
  _room(map, from: (54, 62), to: (59, 66), doorX: 56); // the shop
  _room(map, from: (69, 62), to: (74, 66), doorX: 71); // the bank
  _room(map, from: (66, 82), to: (70, 86), doorX: 68); // the tower
  // Nothing walks on plane 1 outside the tower, so it is blocked but for the
  // one floor.
  final upstairs = Uint8List(regionSize * regionSize)
    ..fillRange(0, regionSize * regionSize, TileFlags.blocked.bits);
  map.addRegion(FlagRegion(RegionKey(1, 1, 1), upstairs));
  for (var y = 82; y <= 86; y++) {
    for (var x = 66; x <= 70; x++) {
      map.setFlags(_at(x, y, 1), TileFlags.open);
    }
  }
  return map;
}

final List<ObjectPlacement> _doorsAndStations = [
  ObjectPlacement(object: ObjectId.door, tile: _at(56, 62)),
  ObjectPlacement(object: ObjectId.door, tile: _at(71, 62)),
  ObjectPlacement(object: ObjectId.door, tile: _at(68, 82)),
  ObjectPlacement(object: ObjectId.bankBooth, tile: _at(70, 66)),
  ObjectPlacement(object: ObjectId.bankBooth, tile: _at(72, 66)),
  ObjectPlacement(object: ObjectId.furnace, tile: _at(50, 60)),
  ObjectPlacement(object: ObjectId.anvil, tile: _at(52, 60)),
  ObjectPlacement(
    object: ObjectId.stairsUp,
    tile: _at(69, 85),
    destination: _at(68, 84, 1),
  ),
  ObjectPlacement(
    object: ObjectId.stairsDown,
    tile: _at(69, 85, 1),
    destination: _at(68, 84),
  ),
];

/// A tree grove in the west, a quarry in the east and fishing spots on the
/// south shore.
final List<ObjectPlacement> _nodes = [
  for (final (x, y) in [
    (24, 64), (26, 67), (28, 62), (30, 66), (25, 70), (29, 70), (32, 63),
    (27, 60), //
  ])
    ObjectPlacement(object: ObjectId.tree, tile: _at(x, y)),
  for (final (x, y) in [
    (100, 60), (102, 62), (104, 60), (101, 65), (103, 66), (106, 63), //
  ])
    ObjectPlacement(object: ObjectId.rock, tile: _at(x, y)),
  for (final (x, y) in [(60, 15), (64, 14), (68, 15)])
    ObjectPlacement(object: ObjectId.fishingSpot, tile: _at(x, y)),
];

NpcSpawn _stands(int npc, int x, int y) => NpcSpawn(npc: npc, tile: _at(x, y));

/// The people stand in the village. Chickens wander by a coop in the north
/// east. Four goblins camp in the south west, which is the four the Guard
/// counts.
final List<NpcSpawn> _creatures = [
  _stands(NpcId.guard, 62, 56),
  _stands(NpcId.shopkeeper, 56, 65),
  _stands(NpcId.lamplighter, 70, 80),
  for (final (x, y) in [(84, 76), (86, 78), (83, 79), (87, 75), (85, 81)])
    NpcSpawn(npc: NpcId.chicken, tile: _at(x, y), radius: 3),
  for (final (x, y) in [(30, 34), (33, 36), (28, 38), (32, 31)])
    NpcSpawn(npc: NpcId.goblin, tile: _at(x, y), radius: 3),
];

/// A new island each call, since the server changes the flags of its map.
/// Five regions: four of sea and ground on plane 0 and one for the floor
/// above the tower.
WorldPlan starterIsland() => WorldPlan(
  map: _terrain(),
  spawn: _at(64, 58),
  objects: [..._doorsAndStations, ..._nodes],
  npcs: _creatures,
);
