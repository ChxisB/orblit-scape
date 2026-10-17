/// What stands on a tile, as bits. A wall flag is a wall along that edge of
/// the tile.
extension type const TileFlags(int bits) {
  static const open = TileFlags(0);
  static const blocked = TileFlags(1);
  static const wallNorth = TileFlags(2);
  static const wallEast = TileFlags(4);
  static const wallSouth = TileFlags(8);
  static const wallWest = TileFlags(16);
  static const water = TileFlags(32);

  /// Tiles that people may enter and creatures may not.
  static const noNpc = TileFlags(64);

  TileFlags operator |(TileFlags other) => TileFlags(bits | other.bits);

  /// Whether any bit of [flag] is set.
  bool has(TileFlags flag) => bits & flag.bits != 0;
}
