import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_progress/orblit_progress.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

extension on TestClient {
  /// The skill changes this player was sent, oldest first.
  List<Changes> get skillDeltas => [
    for (final PrivateState(:delta) in messages.whereType<PrivateState>())
      if (delta.first == skillsDeltaTag)
        Changes.tryDecode(defaultSkills.schema, delta.sublist(1))!,
  ];
}

void main() {
  late ScapeServer server;
  final clients = <TestClient>[];
  final curve = defaultSkills.curve;

  Future<void> tick() async {
    server.loop.runTick();
    await pump();
  }

  Future<TestClient> join(String name) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await tick();
    return client;
  }

  setUp(() {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  test('a new player starts at level 1 with no experience', () async {
    await join('Ana');

    expect(server.skills.xpOf('ana', 'mining'), 0);
    expect(server.skills.levelOf('ana', 'mining'), 1);
  });

  test('a grant that crosses three levels returns each once', () async {
    await join('Ana');

    final levels = server.skills.grant('ana', 'mining', curve.xpForLevel(4));

    expect(levels, [2, 3, 4]);
    expect(server.skills.levelOf('ana', 'mining'), 4);
  });

  test('a grant short of the next level returns nothing', () async {
    await join('Ana');

    expect(
      server.skills.grant('ana', 'mining', curve.xpForLevel(2) - 1),
      isEmpty,
    );
    expect(server.skills.grant('ana', 'mining', 1), [2]);
    expect(server.skills.grant('ana', 'mining', 0), isEmpty);
  });

  test('experience stops at the cap, and so do the levels', () async {
    await join('Ana');

    final levels = server.skills.grant('ana', 'mining', 0xFFFFFFFF);

    expect(levels, [for (var l = 2; l <= 50; l++) l]);
    expect(server.skills.xpOf('ana', 'mining'), curve.xpForLevel(50));
    expect(server.skills.grant('ana', 'mining', 1000), isEmpty);
  });

  test('the owner is sent only the skill that changed', () async {
    final ana = await join('Ana');
    await join('Bo');
    server.skills.grant('ana', 'fishing', 100);
    await tick();

    final delta = ana.skillDeltas.single;
    final index = defaultSkills.schema.placeOf('skill.fishing.xp').slot;

    expect(delta.slots, [index]);
    expect(delta.valueOf(index), 100);
  });

  test('no one else is sent anything', () async {
    await join('Ana');
    final bo = await join('Bo');

    server.skills.grant('ana', 'fishing', 100);
    await tick();

    expect(bo.skillDeltas, isEmpty);
  });

  test('a grant that changes nothing sends nothing', () async {
    final ana = await join('Ana');

    server.skills.grant('ana', 'fishing', 0);
    await tick();

    expect(ana.skillDeltas, isEmpty);
  });

  test('an unknown skill and a negative grant are refused', () async {
    await join('Ana');

    expect(() => server.skills.grant('ana', 'dancing', 1), throwsArgumentError);
    expect(() => server.skills.xpOf('ana', 'dancing'), throwsArgumentError);
    expect(() => server.skills.grant('ana', 'mining', -1), throwsRangeError);
  });

  test('a player who is not in the world gains nothing', () async {
    expect(server.skills.grant('nobody', 'mining', 100), isEmpty);
    expect(server.skills.xpOf('nobody', 'mining'), 0);
    expect(server.skills.levelOf('nobody', 'mining'), 1);
  });

  test('experience starts again for a player who leaves and returns', () async {
    final ana = await join('Ana');
    server.skills.grant('ana', 'mining', 500);
    await ana.dispose();
    for (var i = 0; i < 30; i++) {
      await tick();
    }

    await join('Ana');

    expect(server.skills.xpOf('ana', 'mining'), 0);
  });
}
