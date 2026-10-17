import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

TileMap openWorld() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

TileCoord at(int x, int y) => TileCoord(x, y, 0);

void main() {
  group('TileCoord', () {
    test('rejects a plane, x or y outside the world', () {
      expect(() => TileCoord(-1, 0, 0), throwsArgumentError);
      expect(() => TileCoord(0, -1, 0), throwsArgumentError);
      expect(() => TileCoord(worldSize, 0, 0), throwsArgumentError);
      expect(() => TileCoord(0, worldSize, 0), throwsArgumentError);
      expect(() => TileCoord(0, 0, planeCount), throwsArgumentError);
      expect(() => TileCoord(0, 0, -1), throwsArgumentError);
    });

    test('compares by value', () {
      expect(TileCoord(3, 4, 1), TileCoord(3, 4, 1));
      expect(TileCoord(3, 4, 1).hashCode, TileCoord(3, 4, 1).hashCode);
      expect(TileCoord(3, 4, 1), isNot(TileCoord(3, 4, 2)));
    });

    test('names the region that holds it', () {
      expect(TileCoord(63, 63, 2).region, RegionKey(0, 0, 2));
      expect(TileCoord(64, 0, 0).region, RegionKey(1, 0, 0));
      expect(TileCoord(130, 200, 3).region, RegionKey(2, 3, 3));
    });
  });

  group('canStep', () {
    test('allows a step in each of the eight directions on open ground', () {
      final map = openWorld();
      final centre = at(10, 10);
      for (var dy = -1; dy <= 1; dy++) {
        for (var dx = -1; dx <= 1; dx++) {
          if (dx == 0 && dy == 0) continue;
          expect(map.canStep(centre, at(10 + dx, 10 + dy)), isTrue);
        }
      }
    });

    test('refuses no step, a long step and a step between planes', () {
      final map = openWorld();
      expect(map.canStep(at(5, 5), at(5, 5)), isFalse);
      expect(map.canStep(at(5, 5), at(7, 5)), isFalse);
      expect(map.canStep(at(5, 5), TileCoord(5, 6, 1)), isFalse);
    });

    test('refuses a step into a blocked tile', () {
      final map = openWorld()..setFlags(at(11, 10), TileFlags.blocked);
      expect(map.canStep(at(10, 10), at(11, 10)), isFalse);
      expect(map.canStep(at(11, 10), at(10, 10)), isTrue);
    });

    test('refuses a step into water', () {
      final map = openWorld()..setFlags(at(11, 10), TileFlags.water);
      expect(map.canStep(at(10, 10), at(11, 10)), isFalse);
    });

    test('lets a wall block a step from both sides', () {
      final map = openWorld()..setFlags(at(10, 10), TileFlags.wallNorth);
      expect(map.canStep(at(10, 10), at(10, 11)), isFalse);
      expect(map.canStep(at(10, 11), at(10, 10)), isFalse);
      expect(map.canStep(at(10, 10), at(11, 10)), isTrue);
    });

    test('reads a wall flagged on the other tile of the edge too', () {
      final map = openWorld()..setFlags(at(10, 11), TileFlags.wallSouth);
      expect(map.canStep(at(10, 10), at(10, 11)), isFalse);
      expect(map.canStep(at(10, 11), at(10, 10)), isFalse);
    });

    test('knows each wall side', () {
      for (final (flag, dx, dy) in [
        (TileFlags.wallNorth, 0, 1),
        (TileFlags.wallEast, 1, 0),
        (TileFlags.wallSouth, 0, -1),
        (TileFlags.wallWest, -1, 0),
      ]) {
        final map = openWorld()..setFlags(at(10, 10), flag);
        expect(map.canStep(at(10, 10), at(10 + dx, 10 + dy)), isFalse);
        expect(map.canStep(at(10, 10), at(10 - dx, 10 - dy)), isTrue);
      }
    });

    test('refuses a diagonal past a blocked corner', () {
      final map = openWorld()..setFlags(at(11, 10), TileFlags.blocked);
      expect(map.canStep(at(10, 10), at(11, 11)), isFalse);
      expect(map.canStep(at(11, 11), at(10, 10)), isFalse);
      expect(map.canStep(at(10, 10), at(9, 11)), isTrue);
    });

    test('refuses a diagonal across a wall on either edge', () {
      final map = openWorld()..setFlags(at(10, 10), TileFlags.wallEast);
      expect(map.canStep(at(10, 10), at(11, 11)), isFalse);
      final other = openWorld()..setFlags(at(11, 10), TileFlags.wallNorth);
      expect(other.canStep(at(10, 10), at(11, 11)), isFalse);
    });

    test('keeps a tile only an NPC may not enter open to a player', () {
      final map = openWorld()..setFlags(at(11, 10), TileFlags.noNpc);
      expect(map.canStep(at(10, 10), at(11, 10)), isTrue);
      expect(map.canStep(at(10, 10), at(11, 10), walker: Walker.npc), isFalse);
    });

    test('treats a tile in a region that is not loaded as blocked', () {
      final map = TileMap()..addRegion(FlagRegion.open(RegionKey(0, 0, 0)));
      expect(map.canStep(at(63, 10), at(64, 10)), isFalse);
      expect(map.flagsAt(at(200, 200)), TileFlags.blocked);
    });
  });

  group('setFlags', () {
    test('refuses a tile in a region that is not loaded', () {
      expect(
        () => TileMap().setFlags(at(0, 0), TileFlags.blocked),
        throwsStateError,
      );
    });

    test('combines flags', () {
      final map = openWorld()
        ..setFlags(at(1, 1), TileFlags.blocked | TileFlags.water);
      expect(map.flagsAt(at(1, 1)).has(TileFlags.blocked), isTrue);
      expect(map.flagsAt(at(1, 1)).has(TileFlags.water), isTrue);
      expect(map.flagsAt(at(1, 1)).has(TileFlags.noNpc), isFalse);
    });
  });

  test('builds a 16 by 16 region world', () {
    final watch = Stopwatch()..start();
    final map = TileMap();
    for (var y = 0; y < 16; y++) {
      for (var x = 0; x < 16; x++) {
        map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
      }
    }
    watch.stop();
    // ignore: avoid_print, the figure is recorded and not judged
    print(
      '16x16 regions (${16 * 16 * 64 * 64} tiles): '
      '${watch.elapsedMicroseconds} us',
    );
    expect(map.regionCount, 256);
  });
}
