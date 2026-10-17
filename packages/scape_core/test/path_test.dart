import 'dart:math';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

/// A map whose only open ground is a [size] square at ([x0], [y0]), with
/// each tile in it given random flags. Everything else is blocked.
TileMap randomWindow(Random random, {int x0 = 20, int y0 = 20, int size = 12}) {
  final map = TileMap();
  final region = FlagRegion.open(RegionKey(0, 0, 0));
  for (var i = 0; i < regionSize * regionSize; i++) {
    region.setLocal(i, TileFlags.blocked);
  }
  map.addRegion(region);
  for (var y = y0; y < y0 + size; y++) {
    for (var x = x0; x < x0 + size; x++) {
      var flags = TileFlags.open;
      if (random.nextInt(100) < 22) flags |= TileFlags.blocked;
      if (random.nextInt(100) < 5) flags |= TileFlags.water;
      for (final wall in [
        TileFlags.wallNorth,
        TileFlags.wallEast,
        TileFlags.wallSouth,
        TileFlags.wallWest,
      ]) {
        if (random.nextInt(100) < 6) flags |= wall;
      }
      map.setFlags(at(x, y), flags);
    }
  }
  return map;
}

TileMap openField() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

/// Steps to every tile of the window by sweeping until nothing improves.
/// It shares no code with the search it checks.
Map<TileCoord, int> bruteForce(
  TileMap map,
  TileCoord start,
  int x0,
  int y0,
  int size,
) {
  final dist = <TileCoord, int>{start: 0};
  var changed = true;
  while (changed) {
    changed = false;
    for (final entry in dist.entries.toList()) {
      for (var dy = -1; dy <= 1; dy++) {
        for (var dx = -1; dx <= 1; dx++) {
          final x = entry.key.x + dx;
          final y = entry.key.y + dy;
          if (x < x0 || y < y0 || x >= x0 + size || y >= y0 + size) continue;
          final next = at(x, y);
          if (!map.canStep(entry.key, next)) continue;
          if ((dist[next] ?? 1 << 30) > entry.value + 1) {
            dist[next] = entry.value + 1;
            changed = true;
          }
        }
      }
    }
  }
  return dist;
}

int squaredDistance(TileCoord a, TileCoord b) =>
    (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

void expectWalkable(TileMap map, TileCoord from, List<TileCoord> steps) {
  var here = from;
  for (final step in steps) {
    expect(map.canStep(here, step), isTrue, reason: '$here to $step');
    here = step;
  }
}

void main() {
  group('findPath', () {
    test('is the shortest path on random small maps', () {
      final random = Random(7);
      for (var round = 0; round < 150; round++) {
        final map = randomWindow(random);
        final from = at(20 + random.nextInt(12), 20 + random.nextInt(12));
        final to = at(20 + random.nextInt(12), 20 + random.nextInt(12));
        if (map.flagsAt(from).has(TileFlags.blocked)) continue;

        final expected = bruteForce(map, from, 20, 20, 12);
        final result = findPath(map, from, to);

        expectWalkable(map, from, result.steps);
        if (expected.containsKey(to) && from != to) {
          expect(result.reached, isTrue, reason: 'round $round');
          expect(result.steps.length, expected[to], reason: 'round $round');
          expect(result.steps.last, to);
        } else if (from == to) {
          expect(result.reached, isTrue);
          expect(result.steps, isEmpty);
        } else {
          expect(result.reached, isFalse, reason: 'round $round');
        }
      }
    });

    test('ends on the nearest reachable tile when the target is not', () {
      final random = Random(11);
      var checked = 0;
      for (var round = 0; round < 150 && checked < 40; round++) {
        final map = randomWindow(random);
        final from = at(20 + random.nextInt(12), 20 + random.nextInt(12));
        final to = at(20 + random.nextInt(12), 20 + random.nextInt(12));
        if (map.flagsAt(from).has(TileFlags.blocked)) continue;
        final reachable = bruteForce(map, from, 20, 20, 12);
        if (reachable.containsKey(to)) continue;

        final result = findPath(map, from, to);
        final end = result.steps.isEmpty ? from : result.steps.last;
        final best = reachable.keys
            .map((tile) => squaredDistance(tile, to))
            .reduce(min);

        expect(result.reached, isFalse);
        expect(reachable.containsKey(end), isTrue);
        expect(squaredDistance(end, to), best, reason: 'round $round');
        checked++;
      }
      expect(checked, greaterThan(10));
    });

    test('goes round a wall by the short way', () {
      final map = openField();
      for (var y = 10; y < 20; y++) {
        map.setFlags(at(15, y), TileFlags.blocked);
      }
      final result = findPath(map, at(12, 15), at(18, 15));
      expect(result.reached, isTrue);
      expectWalkable(map, at(12, 15), result.steps);
      expect(result.steps.length, 12);
    });

    test('refuses a target on another plane', () {
      expect(
        () => findPath(openField(), at(1, 1), TileCoord(1, 2, 1)),
        throwsArgumentError,
      );
    });

    test('stays inside the box round the two points', () {
      final map = openField();
      for (var y = 0; y < 40; y++) {
        map.setFlags(at(15, y), TileFlags.blocked);
      }
      final result = findPath(map, at(12, 5), at(18, 5), margin: 2);
      expect(result.reached, isFalse);
    });
  });

  group('Mover', () {
    test('walks a tile a tick and runs two', () {
      final map = openField();
      final path = [at(1, 0), at(2, 0), at(3, 0), at(4, 0)];

      final walker = Mover(at(0, 0))..setPath(path);
      walker.advance(map);
      expect(walker.position, at(1, 0));
      expect(walker.stepsLastTick, [at(1, 0)]);

      final runner = Mover(at(0, 0))
        ..speed = MoveSpeed.run
        ..setPath(path);
      runner.advance(map);
      expect(runner.position, at(2, 0));
      expect(runner.stepsLastTick, [at(1, 0), at(2, 0)]);
    });

    test('stops cleanly on an odd length at a run', () {
      final map = openField();
      final runner = Mover(at(0, 0))
        ..speed = MoveSpeed.run
        ..setPath([at(1, 0), at(2, 0), at(3, 0)]);

      runner.advance(map);
      expect(runner.isMoving, isTrue);
      runner.advance(map);

      expect(runner.position, at(3, 0));
      expect(runner.stepsLastTick, [at(3, 0)]);
      expect(runner.isMoving, isFalse);
      runner.advance(map);
      expect(runner.stepsLastTick, isEmpty);
    });

    test('stops when the world changed under the path', () {
      final map = openField();
      final mover = Mover(at(0, 0))..setPath([at(1, 0), at(2, 0), at(3, 0)]);
      mover.advance(map);
      map.setFlags(at(2, 0), TileFlags.blocked);

      mover.advance(map);

      expect(mover.position, at(1, 0));
      expect(mover.isMoving, isFalse);
    });

    test('refuses a path that does not start beside it', () {
      final mover = Mover(at(0, 0));
      expect(() => mover.setPath([at(2, 0)]), throwsArgumentError);
      expect(() => mover.setPath([at(1, 0), at(3, 0)]), throwsArgumentError);
    });

    test('clearPath stops it', () {
      final mover = Mover(at(0, 0))..setPath([at(1, 0), at(2, 0)]);
      mover.clearPath();
      expect(mover.isMoving, isFalse);
    });

    test('walks a found path to its end', () {
      final map = openField();
      final mover = Mover(at(5, 5));
      mover.setPath(findPath(map, mover.position, at(9, 8)).steps);
      var ticks = 0;
      while (mover.isMoving) {
        mover.advance(map);
        ticks++;
      }
      expect(mover.position, at(9, 8));
      expect(ticks, 4);
    });
  });

  group('Reach', () {
    final object = Footprint(at(30, 30), 3, 3);

    TileMap field() {
      final map = openField();
      for (final tile in object.tiles) {
        map.setFlags(tile, TileFlags.blocked);
      }
      return map;
    }

    bool beside(TileCoord a, TileCoord b) =>
        (a.x - b.x).abs() + (a.y - b.y).abs() == 1;

    test('a mover stops next to a three by three object on every side', () {
      final map = field();
      for (final from in [at(31, 40), at(31, 20), at(40, 31), at(20, 31)]) {
        final result = findPathToReach(map, from, Beside(object));
        expect(result.reached, isTrue, reason: '$from');
        final end = result.steps.last;
        expect(object.tiles.any((tile) => beside(tile, end)), isTrue);
        expect(object.tiles.contains(end), isFalse);
      }
    });

    test('the nearest side is the one it stops at', () {
      final result = findPathToReach(field(), at(31, 40), Beside(object));
      expect(result.steps.last.y, 33);
      expect(result.steps.length, 7);
    });

    test('a corner tile is not beside the object', () {
      final map = field();
      expect(reachSatisfied(map, Beside(object), at(29, 29)), isFalse);
      expect(reachSatisfied(map, Beside(object), at(29, 31)), isTrue);
    });

    test('a wall between the tile and the object does not count', () {
      final map = field()..setFlags(at(29, 31), TileFlags.wallEast);
      expect(reachSatisfied(map, Beside(object), at(29, 31)), isFalse);
      final result = findPathToReach(map, at(20, 31), Beside(object));
      expect(result.steps.last, isNot(at(29, 31)));
      expect(reachSatisfied(map, Beside(object), result.steps.last), isTrue);
    });

    test('standing on a tile is reached only there', () {
      final map = openField();
      final result = findPathToReach(map, at(5, 5), OnTile(at(8, 9)));
      expect(result.steps.last, at(8, 9));
      expect(reachSatisfied(map, OnTile(at(8, 9)), at(8, 8)), isFalse);
    });

    test('a range reaches from afar with a clear line', () {
      final map = field();
      final reach = Within(object, range: 5);
      expect(reachSatisfied(map, reach, at(36, 31)), isTrue);
      expect(reachSatisfied(map, reach, at(38, 31)), isFalse);
      map.setFlags(at(34, 31), TileFlags.wallEast);
      expect(reachSatisfied(map, reach, at(36, 31)), isFalse);
    });

    test('a range finds a tile in line when the first ones are not', () {
      final map = field();
      for (var y = 25; y < 40; y++) {
        map.setFlags(at(36, y), TileFlags.blocked);
      }
      final reach = Within(object, range: 5);
      final result = findPathToReach(map, at(40, 31), reach);
      expect(result.reached, isTrue);
      expect(reachSatisfied(map, reach, result.steps.last), isTrue);
    });
  });

  group('hasClearLine', () {
    test('is clear on open ground and between a tile and itself', () {
      final map = openField();
      expect(hasClearLine(map, at(5, 5), at(12, 9)), isTrue);
      expect(hasClearLine(map, at(5, 5), at(5, 5)), isTrue);
    });

    test('stops at a blocked tile in the way but not on the last one', () {
      final map = openField()..setFlags(at(8, 5), TileFlags.blocked);
      expect(hasClearLine(map, at(5, 5), at(12, 5)), isFalse);
      expect(hasClearLine(map, at(5, 5), at(8, 5)), isTrue);
    });

    test('stops at a wall', () {
      final map = openField()..setFlags(at(8, 5), TileFlags.wallEast);
      expect(hasClearLine(map, at(5, 5), at(12, 5)), isFalse);
      expect(hasClearLine(map, at(12, 5), at(5, 5)), isFalse);
    });
  });
}
