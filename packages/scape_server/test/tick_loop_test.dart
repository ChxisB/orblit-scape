import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/open_world.dart';

const period = Duration(milliseconds: 600);

/// Stops the loop after [ticks] ticks and never sleeps.
final class CountingPacer implements Pacer {
  CountingPacer(this.ticks);

  final int ticks;
  int waits = 0;
  late final ScapeServer server;

  @override
  Future<void> untilNext() async {
    waits++;
    if (waits > ticks) server.loop.stop();
  }
}

void main() {
  late FakeTime time;
  late List<String> lines;

  setUp(() {
    time = FakeTime();
    lines = [];
  });

  ScapeServer build(int ticks) {
    final pacer = CountingPacer(ticks);
    final server = ScapeServer(
      clock: TickClock(period: period, now: time.read),
      pacer: pacer,
      log: lines.add,
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
    );
    pacer.server = server;
    addTearDown(server.dispose);
    return server;
  }

  test('runs the phases in order on every one of 100 ticks', () async {
    final server = build(100);
    final seen = <String>[];
    for (final phase in Phase.values) {
      server.registry.addSystem(phase, () => seen.add(phase.name));
    }

    await server.loop.run();

    expect(server.loop.tick, 100);
    expect(server.host.tick, 100);
    expect(seen, [
      for (var i = 0; i < 100; i++) ...[for (final p in Phase.values) p.name],
    ]);
  });

  test('N5 times each phase, then the host times its own stages', () async {
    final server = build(5);
    await server.loop.run();

    final names = server.host.summary().phases.keys.toList();
    expect(names.take(Phase.values.length), [
      for (final phase in Phase.values) phase.name,
    ]);
    expect(names, contains('capture'));
  });

  test('runs due actions in the actions phase', () async {
    final server = build(3);
    final ran = <int>[];
    final kind = ActionKind('wave', delay: 1);
    server.registry
      ..addKind(kind)
      ..addSystem(Phase.inbound, () {
        if (server.loop.tick == 1) {
          server.registry.actions.submit(
            7,
            kind,
            () => ran.add(server.loop.tick),
          );
        }
      });

    await server.loop.run();

    expect(ran, [2]);
  });

  test('starting with no features registered runs and idles', () async {
    final server = build(20);

    await server.loop.run();

    expect(server.loop.tick, 20);
    expect(server.host.clientCount, 0);
    expect(lines, isEmpty);
  });

  test('a late tick logs the summary of where the time went', () async {
    final server = build(3);
    server.registry.addSystem(Phase.movement, () => time.advance(period * 2));

    await server.loop.run();

    expect(lines, hasLength(1));
    expect(lines.single, contains('late tick'));
    expect(lines.single, contains('movement'));
    expect(lines.single, contains('1200.0 ms'));
  });

  test('stop ends the loop before another tick runs', () async {
    final server = build(1000);
    server.registry.addSystem(Phase.npcs, server.loop.stop);

    await server.loop.run();

    expect(server.loop.tick, 1);
  });
}
