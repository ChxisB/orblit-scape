import 'package:scape_bots/scape_bots.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

TileCoord _at(int x, int y) => TileCoord(x, y, 0);

/// Open ground with a wall of blocked tiles six tiles out from the spawn on
/// every side, and a few blocked tiles inside it.
TileMap _walled() {
  final map = openGround();
  for (var i = -6; i <= 6; i++) {
    for (final edge in [-6, 6]) {
      map
        ..setFlags(_at(32 + i, 32 + edge), TileFlags.blocked)
        ..setFlags(_at(32 + edge, 32 + i), TileFlags.blocked);
    }
  }
  for (final (x, y) in [(36, 32), (34, 36), (29, 36)]) {
    map.setFlags(_at(x, y), TileFlags.blocked);
  }
  return map;
}

void main() {
  final runs = <BotRun>[];

  Future<BotRun> start({
    required int bots,
    int seed = 1,
    int reach = 12,
    TileMap? map,
  }) async {
    final run = await BotRun.start(
      bots: bots,
      seed: seed,
      reach: reach,
      map: map,
    );
    runs.add(run);
    return run;
  }

  tearDown(() async {
    for (final run in runs) {
      await run.dispose();
    }
    runs.clear();
  });

  test('every bot logs in and each tick is timed', () async {
    final run = await start(bots: 10);
    await run.step(20);

    final report = run.report();
    expect(report.joined, 10);
    expect(report.tickTimes, hasLength(20));
    expect(report.problems, isEmpty);
    expect(run.probe.players(), hasLength(10));
  });

  test('bots walk into walls without breaking the server', () async {
    final map = _walled();
    final run = await start(bots: 20, map: map);
    await run.step(60);

    final report = run.report();
    expect(report.joined, 20);
    expect(report.problems, isEmpty);
    expect(report.tickTimes, hasLength(60));
    final tiles = [
      for (final name in run.probe.players()) run.probe.player(name)!.tile,
    ];
    expect(tiles.where((tile) => tile != _at(32, 32)), isNotEmpty);
    for (final tile in tiles) {
      expect((tile.x - 32).abs(), lessThan(6));
      expect((tile.y - 32).abs(), lessThan(6));
      expect(map.flagsAt(tile), isNot(TileFlags.blocked));
    }
  });

  test('bots fight what they find', () async {
    final run = await start(bots: 20);
    await run.step(80);

    final fought = [
      for (final name in run.probe.players())
        if (run.probe.player(name)!.experience.isNotEmpty) name,
    ];
    expect(fought, isNotEmpty);
  });

  test('bots walk to tiles far outside the world without breaking', () async {
    final run = await start(bots: 10, reach: 1000);
    await run.step(30);

    expect(run.report().joined, 10);
    expect(run.report().problems, isEmpty);
  });

  test('bots with nothing to fight or use keep going', () async {
    final run = await start(bots: 10);
    for (var i = 0; i < 4; i++) {
      run.server.objects.remove(run.server.objects.at(_at(30 + i, 34))!);
    }
    [...run.server.npcs.standing].forEach(run.server.npcs.kill);
    await run.step(30);

    expect(run.server.npcs.standing, isEmpty);
    expect(run.report().joined, 10);
    expect(run.report().problems, isEmpty);
  });

  test('a bot sees the world as it is a tick or two ago', () async {
    final run = await start(bots: 1);
    await run.step(5);
    final session = run.bots.single.session;
    expect(session.npcs, isNotEmpty);

    [...run.server.npcs.standing].forEach(run.server.npcs.kill);
    await run.step(10);

    expect(session.npcs, isEmpty);
  });

  test('a bot the server dropped is not counted as joined', () async {
    final run = await start(bots: 3);
    await run.step(5);
    await run.server.host.removeClient('bot-0001');
    await run.step(15);

    expect(run.report().joined, 2);
    expect(run.report().healthy, isFalse);
  });

  test('a tick that throws ends the run and is recorded', () async {
    final run = await start(bots: 2);
    await run.step(3);
    run.server.loop.registry.addSystem(Phase.npcs, () => throw StateError('x'));
    await run.step(5);

    final report = run.report();
    expect(report.tickTimes, hasLength(3));
    expect(report.problems.single, contains('threw'));
    expect(report.healthy, isFalse);
  });

  test('the same seed plays out the same way', () async {
    Future<List<TileCoord>> play() async {
      final run = await start(bots: 8, seed: 5);
      await run.step(40);
      return [
        for (final name in run.probe.players()) run.probe.player(name)!.tile,
      ];
    }

    expect(await play(), await play());
  });
}
