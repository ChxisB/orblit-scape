import 'flag_region.dart';
import 'tile_coord.dart';
import 'tile_flags.dart';

/// Who is stepping. Creatures keep out of tiles flagged [TileFlags.noNpc].
enum Mover { player, npc }

/// The flags of every loaded region, and the rules for one step over them.
final class TileMap {
  final Map<RegionKey, FlagRegion> _regions = {};

  int get regionCount => _regions.length;

  /// Loads [region], replacing one with the same key.
  void addRegion(FlagRegion region) => _regions[region.key] = region;

  FlagRegion? regionAt(RegionKey key) => _regions[key];

  /// The flags on [tile], or [TileFlags.blocked] when its region is not
  /// loaded, so nobody walks off the edge of what is known.
  TileFlags flagsAt(TileCoord tile) {
    final region = _regions[tile.region];
    return region == null
        ? TileFlags.blocked
        : region.flagsAtLocal(tile.localIndex);
  }

  void setFlags(TileCoord tile, TileFlags flags) {
    final region = _regions[tile.region];
    if (region == null) {
      throw StateError('${tile.region} is not loaded.');
    }
    region.setLocal(tile.localIndex, flags);
  }

  /// Whether [mover] may step from [from] to the tile beside it, [to].
  ///
  /// A diagonal needs both tiles at its corners open and no wall on any of
  /// the four edges round them. A step to the same tile, past the next tile
  /// or to another plane is never allowed.
  bool canStep(TileCoord from, TileCoord to, {Mover mover = Mover.player}) {
    final dx = to.x - from.x;
    final dy = to.y - from.y;
    if (from.plane != to.plane || dx.abs() > 1 || dy.abs() > 1) return false;
    if (dx == 0 && dy == 0) return false;
    if (dx == 0 || dy == 0) return _canStepStraight(from, to, mover);
    final alongX = TileCoord(to.x, from.y, from.plane);
    final alongY = TileCoord(from.x, to.y, from.plane);
    return _canStepStraight(from, alongX, mover) &&
        _canStepStraight(alongX, to, mover) &&
        _canStepStraight(from, alongY, mover) &&
        _canStepStraight(alongY, to, mover);
  }

  bool _canStepStraight(TileCoord from, TileCoord to, Mover mover) {
    final dx = to.x - from.x;
    final dy = to.y - from.y;
    final entering = flagsAt(to);
    if (entering.has(TileFlags.blocked | TileFlags.water)) return false;
    if (mover == Mover.npc && entering.has(TileFlags.noNpc)) return false;
    return !flagsAt(from).has(_wallToward(dx, dy)) &&
        !entering.has(_wallToward(-dx, -dy));
  }

  static TileFlags _wallToward(int dx, int dy) {
    if (dy > 0) return TileFlags.wallNorth;
    if (dy < 0) return TileFlags.wallSouth;
    return dx > 0 ? TileFlags.wallEast : TileFlags.wallWest;
  }
}
