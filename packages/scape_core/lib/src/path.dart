import 'dart:math';
import 'dart:typed_data';

import 'reach.dart';
import 'tile_coord.dart';
import 'tile_map.dart';

/// How far past the two points a search may look, in tiles.
const int defaultPathMargin = 16;

const List<(int, int)> _offsets = [
  (-1, 0),
  (1, 0),
  (0, -1),
  (0, 1),
  (-1, -1),
  (1, -1),
  (-1, 1),
  (1, 1),
];

/// The tiles to walk, one step each and not counting where the walk starts.
///
/// When the target cannot be reached, [reached] is false and [steps] lead to
/// the reachable tile nearest to it.
final class PathResult {
  PathResult(List<TileCoord> steps, {required this.reached})
    : steps = List.unmodifiable(steps);

  final List<TileCoord> steps;
  final bool reached;
}

/// The shortest walk from [from] to [to] in steps, counting a diagonal as
/// one.
PathResult findPath(
  TileMap map,
  TileCoord from,
  TileCoord to, {
  Walker walker = Walker.player,
  int margin = defaultPathMargin,
}) => findPathToReach(map, from, OnTile(to), walker: walker, margin: margin);

/// The shortest walk from [from] to any tile that satisfies [reach].
///
/// The search stays inside the box round [from] and the target, grown by
/// [margin] tiles on each side, so a target behind a long wall is called
/// unreachable instead of costing the whole map.
PathResult findPathToReach(
  TileMap map,
  TileCoord from,
  Reach reach, {
  Walker walker = Walker.player,
  int margin = defaultPathMargin,
}) {
  if (from.plane != reach.target.origin.plane) {
    throw ArgumentError('A path stays on one plane.');
  }
  return _Search(map, from, reach, walker, margin).run();
}

/// The tiles a search may look at: the box round the start and the target,
/// grown by a margin and cut to the world.
final class _Box {
  _Box(TileCoord from, Footprint target, int margin)
    : minX = max(0, min(from.x, target.origin.x) - margin),
      minY = max(0, min(from.y, target.origin.y) - margin),
      maxX = min(
        worldSize,
        max(from.x + 1, target.origin.x + target.width) + margin,
      ),
      maxY = min(
        worldSize,
        max(from.y + 1, target.origin.y + target.height) + margin,
      );

  final int minX;
  final int minY;
  final int maxX;
  final int maxY;

  int get width => maxX - minX;
  int get height => maxY - minY;

  bool contains(int x, int y) => x >= minX && x < maxX && y >= minY && y < maxY;

  int indexOf(int x, int y) => (y - minY) * width + (x - minX);
}

final class _Search {
  _Search(this.map, this.from, this.reach, this.walker, int margin)
    : _box = _Box(from, reach.target, margin) {
    final size = _box.width * _box.height;
    _parent = Int32List(size)..fillRange(0, size, -1);
  }

  final TileMap map;
  final TileCoord from;
  final Reach reach;
  final Walker walker;
  final _Box _box;
  late final Int32List _parent;

  TileCoord _tile(int index) => TileCoord(
    _box.minX + index % _box.width,
    _box.minY + index ~/ _box.width,
    from.plane,
  );

  PathResult run() {
    final start = _box.indexOf(from.x, from.y);
    _parent[start] = start;
    final queue = <int>[start];
    var nearest = start;
    var nearestDistance = reach.target.distanceSquared(from);
    for (var head = 0; head < queue.length; head++) {
      final current = _tile(queue[head]);
      if (reachSatisfied(map, reach, current)) {
        return PathResult(_walkBack(queue[head]), reached: true);
      }
      final distance = reach.target.distanceSquared(current);
      if (distance < nearestDistance) {
        nearest = queue[head];
        nearestDistance = distance;
      }
      _expand(current, queue[head], queue);
    }
    return PathResult(_walkBack(nearest), reached: false);
  }

  void _expand(TileCoord current, int index, List<int> queue) {
    for (final (dx, dy) in _offsets) {
      final x = current.x + dx;
      final y = current.y + dy;
      if (!_box.contains(x, y)) continue;
      final next = _box.indexOf(x, y);
      if (_parent[next] != -1) continue;
      if (!map.canStep(current, TileCoord(x, y, from.plane), walker: walker)) {
        continue;
      }
      _parent[next] = index;
      queue.add(next);
    }
  }

  List<TileCoord> _walkBack(int end) {
    final steps = <TileCoord>[];
    for (var at = end; _parent[at] != at; at = _parent[at]) {
      steps.add(_tile(at));
    }
    return steps.reversed.toList();
  }
}
