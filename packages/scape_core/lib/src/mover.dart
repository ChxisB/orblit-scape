import 'dart:collection';

import 'tile_coord.dart';
import 'tile_map.dart';

/// How many tiles a mover covers in one tick.
enum MoveSpeed {
  walk(1),
  run(2);

  const MoveSpeed(this.tilesPerTick);

  final int tilesPerTick;
}

/// Something that walks a queue of tiles, a tick at a time.
///
/// It knows its speed and nothing about why it has one. Run energy and the
/// choice between walking and running belong to whatever owns the mover.
final class Mover {
  Mover(this.position, {this.walker = Walker.player});

  final Walker walker;
  MoveSpeed speed = MoveSpeed.walk;

  TileCoord position;
  final Queue<TileCoord> _queue = Queue();
  List<TileCoord> _lastSteps = const [];

  bool get isMoving => _queue.isNotEmpty;

  /// The tiles entered by the last [advance], in order.
  List<TileCoord> get stepsLastTick => _lastSteps;

  /// Replaces the queue with [path], whose first tile has to be beside
  /// [position] and each next tile beside the one before.
  void setPath(List<TileCoord> path) {
    var here = position;
    for (final tile in path) {
      if (tile.plane != here.plane ||
          (tile.x - here.x).abs() > 1 ||
          (tile.y - here.y).abs() > 1 ||
          tile == here) {
        throw ArgumentError('$tile is not one step from $here.');
      }
      here = tile;
    }
    _queue
      ..clear()
      ..addAll(path);
  }

  void clearPath() => _queue.clear();

  /// Takes up to [MoveSpeed.tilesPerTick] steps along the queue. A step the
  /// map no longer allows, because a door shut or something was built, ends
  /// the walk where it stands.
  void advance(TileMap map) {
    final taken = <TileCoord>[];
    while (taken.length < speed.tilesPerTick && _queue.isNotEmpty) {
      final next = _queue.first;
      if (!map.canStep(position, next, walker: walker)) {
        _queue.clear();
        break;
      }
      _queue.removeFirst();
      position = next;
      taken.add(next);
    }
    _lastSteps = taken;
  }
}
