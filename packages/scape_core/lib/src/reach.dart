import 'line_of_sight.dart';
import 'tile_coord.dart';
import 'tile_map.dart';

/// A rectangle of tiles on one plane, such as a tree or a door frame, named
/// by its south-west tile.
final class Footprint {
  Footprint(this.origin, this.width, this.height) {
    if (width < 1 || height < 1) {
      throw ArgumentError('A footprint is at least one tile each way.');
    }
    if (origin.x + width > worldSize || origin.y + height > worldSize) {
      throw ArgumentError('The footprint runs off the world.');
    }
  }

  /// One tile.
  Footprint.single(TileCoord tile) : this(tile, 1, 1);

  final TileCoord origin;
  final int width;
  final int height;

  Iterable<TileCoord> get tiles sync* {
    for (var y = origin.y; y < origin.y + height; y++) {
      for (var x = origin.x; x < origin.x + width; x++) {
        yield TileCoord(x, y, origin.plane);
      }
    }
  }

  /// The tile of the footprint nearest [from], on the footprint's plane.
  TileCoord closestTile(TileCoord from) => TileCoord(
    from.x.clamp(origin.x, origin.x + width - 1),
    from.y.clamp(origin.y, origin.y + height - 1),
    origin.plane,
  );

  /// The squared distance in tiles from [from] to [closestTile].
  int distanceSquared(TileCoord from) {
    final near = closestTile(from);
    final dx = from.x - near.x;
    final dy = from.y - near.y;
    return dx * dx + dy * dy;
  }
}

/// Where a mover has to stand to do something. A small value, not a flag.
sealed class Reach {
  const Reach(this.target);

  final Footprint target;
}

/// On one tile.
final class OnTile extends Reach {
  OnTile(TileCoord tile) : super(Footprint.single(tile));
}

/// On a tile that shares an edge with [target], with no wall between. A
/// tile at a corner does not count.
final class Beside extends Reach {
  const Beside(super.target);
}

/// Within [range] tiles of [target], with a clear line to it.
final class Within extends Reach {
  Within(super.target, {required this.range}) {
    if (range < 1) throw ArgumentError.value(range, 'range', 'At least 1.');
  }

  final int range;
}

/// Whether standing on [at] satisfies [reach].
bool reachSatisfied(TileMap map, Reach reach, TileCoord at) {
  if (at.plane != reach.target.origin.plane) return false;
  final near = reach.target.closestTile(at);
  return switch (reach) {
    OnTile() => at == near,
    Beside() => _sharesEdge(map, at, near),
    Within(:final range) =>
      _chebyshev(at, near) <= range && hasClearLine(map, at, near),
  };
}

bool _sharesEdge(TileMap map, TileCoord at, TileCoord near) =>
    (at.x - near.x).abs() + (at.y - near.y).abs() == 1 &&
    map.edgeOpen(at, near);

int _chebyshev(TileCoord a, TileCoord b) =>
    (a.x - b.x).abs() > (a.y - b.y).abs()
    ? (a.x - b.x).abs()
    : (a.y - b.y).abs();
