/// Tiles across and down one region.
const int regionSize = 64;

/// Tiles from one edge of the world to the other, on both axes: 256 regions.
const int worldSize = 256 * regionSize;

/// Floors that can stack on top of each other.
const int planeCount = 4;

/// Regions from one edge of the world to the other.
const int regionsAcross = worldSize ~/ regionSize;

/// Which 64 by 64 square of one plane a tile is in.
///
/// Region (0, 0) holds tiles 0 to 63 on both axes. A terrain whose regions
/// are 64 texels across with one texel to a tile cuts the ground the same
/// way, with [y] as its z.
final class RegionKey {
  RegionKey(this.x, this.y, this.plane) {
    if (x < 0 || x >= regionsAcross || y < 0 || y >= regionsAcross) {
      throw ArgumentError('Region ($x, $y) is outside the world.');
    }
    if (plane < 0 || plane >= planeCount) {
      throw ArgumentError.value(
        plane,
        'plane',
        'There are $planeCount planes.',
      );
    }
  }

  final int x;
  final int y;
  final int plane;

  @override
  bool operator ==(Object other) =>
      other is RegionKey &&
      other.x == x &&
      other.y == y &&
      other.plane == plane;

  @override
  int get hashCode => Object.hash(x, y, plane);

  @override
  String toString() => 'RegionKey($x, $y, $plane)';
}

/// One tile of the world. [y] grows to the north.
final class TileCoord {
  TileCoord(this.x, this.y, this.plane) {
    if (x < 0 || x >= worldSize || y < 0 || y >= worldSize) {
      throw ArgumentError('Tile ($x, $y) is outside the world.');
    }
    if (plane < 0 || plane >= planeCount) {
      throw ArgumentError.value(
        plane,
        'plane',
        'There are $planeCount planes.',
      );
    }
  }

  final int x;
  final int y;
  final int plane;

  RegionKey get region => RegionKey(x ~/ regionSize, y ~/ regionSize, plane);

  /// The place of this tile in its region's flag list, row by row.
  int get localIndex => (y % regionSize) * regionSize + x % regionSize;

  @override
  bool operator ==(Object other) =>
      other is TileCoord &&
      other.x == x &&
      other.y == y &&
      other.plane == plane;

  @override
  int get hashCode => Object.hash(x, y, plane);

  @override
  String toString() => 'TileCoord($x, $y, $plane)';
}
