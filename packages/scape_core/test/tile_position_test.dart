import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('a tile round-trips through the bytes', () {
    final bytes = Uint8List(tilePositionFloats * 4);
    writeTilePosition(bytes, TileCoord(16383, 12, 3));

    expect(readTilePoint(bytes), (x: 16383.0, y: 12.0, plane: 3));
    expect(tileNearest(readTilePoint(bytes)), TileCoord(16383, 12, 3));
  });

  test('puts x first and y last, so a grid reads x and z', () {
    final bytes = Uint8List(12);
    writeTilePosition(bytes, TileCoord(5, 9, 2));

    final data = ByteData.sublistView(bytes);
    expect(data.getFloat32(0, Endian.little), 5);
    expect(data.getFloat32(4, Endian.little), 2);
    expect(data.getFloat32(8, Endian.little), 9);
  });

  test('a point between two tiles rounds to the nearer', () {
    expect(tileNearest((x: 3.4, y: 7.6, plane: 0)), TileCoord(3, 8, 0));
  });
}
