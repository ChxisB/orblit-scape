import 'dart:typed_data';

import 'tile_coord.dart';

/// The name of the replicated component holding where an actor stands.
const String tilePositionName = 'TilePosition';

/// Floats in the component: x, the plane, then y. That puts a tile where an
/// interest grid reads a position, as x, up, z.
const int tilePositionFloats = 3;

/// Where an actor is drawn, in tiles. Between two ticks it is a blend of two
/// tiles, so the numbers are not whole.
typedef TilePoint = ({double x, double y, int plane});

/// Writes [tile] into the component's bytes.
void writeTilePosition(Uint8List bytes, TileCoord tile) {
  ByteData.sublistView(bytes)
    ..setFloat32(0, tile.x.toDouble(), Endian.little)
    ..setFloat32(4, tile.plane.toDouble(), Endian.little)
    ..setFloat32(8, tile.y.toDouble(), Endian.little);
}

/// Reads the component's bytes as they are, with no rounding.
TilePoint readTilePoint(Uint8List bytes) {
  final data = ByteData.sublistView(bytes);
  return (
    x: data.getFloat32(0, Endian.little),
    y: data.getFloat32(8, Endian.little),
    plane: data.getFloat32(4, Endian.little).round(),
  );
}

/// The tile a point is nearest to.
TileCoord tileNearest(TilePoint point) =>
    TileCoord(point.x.round(), point.y.round(), point.plane);
