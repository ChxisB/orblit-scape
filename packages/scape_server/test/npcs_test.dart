import 'dart:math';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

int gap(TileCoord a, TileCoord b) => max((a.x - b.x).abs(), (a.y - b.y).abs());

/// Dice that answer from a list, over and over.
final class _Rolls implements Random {
  _Rolls(this._values);

  final List<double> _values;
  int drawn = 0;

  @override
  double nextDouble() => _values[drawn++ % _values.length];

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => throw UnimplementedError();
}

void main() {
  late ScapeServer server;
  late TileMap map;
  final clients = <TestClient>[];

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  Future<TestClient> join(String name) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await tick();
    return client;
  }

  void start({NpcSet? npcs, Random? random}) {
    map = openWorld()..addRegion(FlagRegion.open(RegionKey(0, 0, 1)));
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: map,
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: npcs ?? defaultNpcs,
      story: defaultStory,
      combat: defaultCombat,
      shops: defaultShops,
      random: random ?? Random(7),
    );
  }

  PlacedNpc place(int npc, TileCoord tile, {int radius = 0}) =>
      server.npcs.spawn(NpcSpawn(npc: npc, tile: tile, radius: radius));

  TileCoord whereIs(TestClient client) =>
      client.tileOf(client.messages.whereType<Avatar>().single.networkId)!;

  /// A hound that sees [sight] tiles and goes [leash] from home.
  NpcSet hound({required int sight, required int leash}) => NpcSet(
    npcs: [
      NpcDef(
        id: 1,
        name: 'Hound',
        behaviour: Aggressive(sight: sight, leash: leash),
      ),
    ],
  );

  /// Ticks [times] and calls [check] after each one.
  Future<void> tickChecking(int times, void Function() check) async {
    for (var i = 0; i < times; i++) {
      await tick();
      check();
    }
  }

  setUp(start);

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('spawning', () {
    test('a creature is told to a player near it', () async {
      final ana = await join('Ana');
      final guard = place(NpcId.guard, at(36, 32));

      await tick(2);

      expect(ana.npcs, {guard.networkId: NpcId.guard});
      expect(ana.tileOf(guard.networkId), at(36, 32));
    });

    test('refuses a creature that does not exist', () {
      expect(
        () => server.npcs.spawn(NpcSpawn(npc: 99, tile: at(36, 32))),
        throwsArgumentError,
      );
    });

    test('refuses ground a creature may not stand on', () {
      map
        ..setFlags(at(36, 32), TileFlags.blocked)
        ..setFlags(at(37, 32), TileFlags.water)
        ..setFlags(at(38, 32), TileFlags.noNpc);

      for (final x in [36, 37, 38]) {
        expect(() => place(NpcId.guard, at(x, 32)), throwsArgumentError);
      }
      expect(() => place(NpcId.guard, at(39, 32)), returnsNormally);
    });
  });

  group('a guard', () {
    test('stays where it is put whatever its radius', () async {
      await join('Ana');
      final guard = place(NpcId.guard, at(36, 32), radius: 5);

      await tickChecking(80, () => expect(guard.tile, at(36, 32)));
    });
  });

  group('a chicken', () {
    test('wanders and stays inside its radius', () async {
      await join('Ana');
      final chicken = place(NpcId.chicken, at(40, 32), radius: 4);
      final seen = <TileCoord>{};

      await tickChecking(600, () {
        seen.add(chicken.tile);
        expect(gap(chicken.tile, chicken.home), lessThanOrEqualTo(4));
      });

      final home = chicken.home;
      expect(seen.length, greaterThan(5));
      expect(seen.any((tile) => gap(tile, home) == 4), isTrue);
      expect(seen.any((tile) => tile.x < home.x), isTrue);
      expect(seen.any((tile) => tile.x > home.x), isTrue);
      expect(seen.any((tile) => tile.y < home.y), isTrue);
      expect(seen.any((tile) => tile.y > home.y), isTrue);
    });

    test('wanders along the edge of the world', () async {
      final ana = await join('Ana');
      final chicken = place(NpcId.chicken, at(0, 0), radius: 4);
      await ana.send(Walk(at(6, 6)));
      await tick(40);
      final seen = <TileCoord>{};

      await tickChecking(300, () => seen.add(chicken.tile));

      expect(seen.length, greaterThan(3));
    });

    test('stays home when the dice are over the chance', () async {
      start(random: _Rolls([0.99]));
      await join('Ana');
      final chicken = place(NpcId.chicken, at(40, 32), radius: 4);

      await tick(50);

      expect(chicken.tile, chicken.home);
    });

    test('sets off when the dice are under the chance', () async {
      start(random: _Rolls([0.0]));
      await join('Ana');
      final chicken = place(NpcId.chicken, at(40, 32), radius: 4);

      await tick(5);

      expect(chicken.tile, isNot(chicken.home));
    });

    test('is shown where it stands as it walks', () async {
      final ana = await join('Ana');
      final chicken = place(NpcId.chicken, at(36, 32), radius: 4);

      await tickChecking(
        100,
        () => expect(ana.tileOf(chicken.networkId), chicken.tile),
      );
    });

    test('keeps out of tiles flagged against creatures', () async {
      for (var y = 0; y < 128; y++) {
        for (var x = 41; x <= 43; x++) {
          map.setFlags(at(x, y), TileFlags.noNpc);
        }
      }
      await join('Ana');
      final chicken = place(NpcId.chicken, at(40, 32), radius: 6);

      await tickChecking(600, () => expect(chicken.tile.x, lessThan(41)));
    });
  });

  group('a goblin', () {
    test('follows a player in sight and stops beside them', () async {
      final ana = await join('Ana');
      final goblin = place(NpcId.goblin, at(36, 32));

      await tick(10);

      final player = ana.tileOf(
        ana.messages.whereType<Avatar>().single.networkId,
      )!;
      expect(
        (goblin.tile.x - player.x).abs() + (goblin.tile.y - player.y).abs(),
        1,
      );

      final stood = goblin.tile;
      await tick(10);
      expect(goblin.tile, stood);
    });

    test('ignores a player out of sight', () async {
      await join('Ana');
      final goblin = place(NpcId.goblin, at(45, 32));

      await tick(30);

      expect(goblin.tile, at(45, 32));
    });

    test('gives up on a player past its leash', () async {
      start(npcs: hound(sight: 4, leash: 5), random: _Rolls([0.99]));
      final ana = await join('Ana');
      final dog = place(1, at(40, 32), radius: 5);

      await ana.send(Walk(at(45, 32)));
      await tick(15);
      expect(dog.tile, at(44, 32));

      await ana.send(Walk(at(46, 32)));
      await tick(15);
      expect(dog.tile, dog.home);
    });

    test('goes for the nearer of two players', () async {
      final ana = await join('Ana');
      final ben = await join('Ben');
      await ben.send(Walk(at(32, 38)));
      await tick(10);
      expect(whereIs(ana), at(32, 32));
      expect(whereIs(ben), at(32, 38));

      final goblin = place(NpcId.goblin, at(36, 33));
      await tick();

      expect(goblin.tile.y, lessThanOrEqualTo(33));
    });

    test('turns round to follow a player who walks past', () async {
      final ana = await join('Ana');
      final goblin = place(NpcId.goblin, at(37, 32));
      await ana.send(Walk(at(44, 32)));
      await tick(8);

      await tickChecking(
        8,
        () => expect(gap(goblin.tile, whereIs(ana)), lessThanOrEqualTo(2)),
      );
    });

    test('goes round a wall to a player and back home', () async {
      start(random: _Rolls([0.99]));
      for (var y = 0; y < 128; y++) {
        if (y != 25) map.setFlags(at(42, y), TileFlags.noNpc);
      }
      final ana = await join('Ana');
      final goblin = place(NpcId.goblin, at(40, 32));

      await ana.send(Walk(at(44, 32)));
      await tick(40);
      expect(gap(goblin.tile, whereIs(ana)), 1);

      await ana.send(Walk(at(53, 32)));
      await tick(60);
      expect(goblin.tile, goblin.home);
    });

    test('goes home when the player leaves its leash', () async {
      start(random: _Rolls([0.99]));
      final ana = await join('Ana');
      final goblin = place(NpcId.goblin, at(40, 32), radius: 3);
      await ana.send(Walk(at(38, 32)));
      await tick(4);
      expect(goblin.tile, isNot(goblin.home));

      await ana.send(Walk(at(32, 60)));
      await tick(30);

      expect(goblin.tile, goblin.home);
    });

    test('never passes its leash while a player pulls at it', () async {
      final ana = await join('Ana');
      final goblin = place(NpcId.goblin, at(40, 32), radius: 3);

      for (var round = 0; round < 60; round++) {
        await ana.send(Walk(at(round.isEven ? 52 : 53, 32)));
        await tickChecking(
          4,
          () => expect(gap(goblin.tile, goblin.home), lessThanOrEqualTo(12)),
        );
      }
    });

    test('cuts a detour short when it would pass the leash', () async {
      start(
        npcs: NpcSet(
          npcs: [
            NpcDef(
              id: 1,
              name: 'Hound',
              behaviour: Aggressive(sight: 8, leash: 8),
            ),
          ],
        ),
      );
      for (var y = 20; y < 90; y++) {
        map.setFlags(at(44, y), TileFlags.blocked);
      }
      await join('Ana');
      final hound = place(1, at(40, 32));
      final ana = clients.first;
      await ana.send(Walk(at(48, 32)));

      await tickChecking(100, () {
        expect(gap(hound.tile, hound.home), lessThanOrEqualTo(8));
        expect(hound.tile.x, lessThan(44));
      });
    });

    test(
      'does not follow a player into ground flagged against creatures',
      () async {
        for (var y = 28; y <= 36; y++) {
          for (var x = 30; x <= 35; x++) {
            map.setFlags(at(x, y), TileFlags.noNpc);
          }
        }
        await join('Ana');
        final goblin = place(NpcId.goblin, at(38, 32));

        await tickChecking(30, () => expect(goblin.tile.x, greaterThan(35)));
      },
    );

    test('is not drawn to a player on another plane', () async {
      await join('Ana');
      final goblin = place(NpcId.goblin, TileCoord(36, 32, 1));

      await tick(20);

      expect(goblin.tile, TileCoord(36, 32, 1));
    });
  });

  group('a region nobody is near', () {
    test('does not tick its creatures', () async {
      final rolls = _Rolls([0.0]);
      start(random: rolls);
      final chicken = place(NpcId.chicken, at(40, 32), radius: 5);

      await tick(50);

      expect(rolls.drawn, 0);
      expect(chicken.tile, chicken.home);
    });

    test('puts a creature back at home when it wakes', () async {
      final ana = await join('Ana');
      final chicken = place(NpcId.chicken, at(40, 32), radius: 6);
      await tickChecking(200, () {});
      expect(chicken.tile, isNot(chicken.home));

      await ana.send(Walk(at(100, 32)));
      while (server.active.isActiveAt(40, 32)) {
        await tick();
      }
      final stood = chicken.tile;
      await tick(20);
      expect(chicken.tile, stood);

      await ana.send(Walk(at(36, 32)));
      while (!server.active.isActiveAt(40, 32)) {
        await tick();
      }

      expect(chicken.tile, chicken.home);
    });
  });

  test('a tick with a thousand creatures stays inside its budget', () async {
    await join('Ana');
    final random = Random(1);
    for (var i = 0; i < 1000; i++) {
      place(
        i % 10 == 0 ? NpcId.goblin : NpcId.chicken,
        at(10 + random.nextInt(90), 10 + random.nextInt(90)),
        radius: 4,
      );
    }
    await tick(5);

    final clock = Stopwatch()..start();
    var slowest = Duration.zero;
    for (var i = 0; i < 30; i++) {
      final before = clock.elapsed;
      server.loop.runTick();
      final took = clock.elapsed - before;
      if (took > slowest) slowest = took;
      await pump();
    }
    final average = clock.elapsed ~/ 30;

    // ignore: avoid_print, the figure is recorded in the task file.
    print('1000 creatures: average $average a tick, slowest $slowest');
    expect(server.npcs.count, 1000);
    expect(average, lessThan(tickPeriod ~/ 2));
  });
}
