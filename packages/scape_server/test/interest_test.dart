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

void main() {
  late ScapeServer server;
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

  int positionBytes(String name) => server.host
      .summary()
      .bytesSent[nameKey(name)]!
      .columns[tilePositionName]!;

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
      npcs: defaultNpcs,
      combat: defaultCombat,
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('interest', () {
    test('players on the same ground see each other', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await tick();

      expect(ana.client.entityCount, 2);
      expect(bo.client.entityCount, 2);
    });

    test('a player who walks out of the ring is removed from view', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await bo.send(Walk(at(100, 32)));

      await tick(80);

      expect(ana.client.entityCount, 1);
      expect(bo.client.entityCount, 1);
    });

    test('a player far away is sent none of the movement', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final cy = await join('Cy');
      await cy.send(Walk(at(110, 32)));
      await tick(80);
      final before = {
        for (final n in ['Bo', 'Cy']) n: positionBytes(n),
      };

      await ana.send(Walk(at(37, 32)));
      await tick(6);

      expect(positionBytes('Bo'), greaterThan(before['Bo']!));
      expect(positionBytes('Cy'), before['Cy']);
      expect(bo.client.entityCount, 2);
      expect(cy.client.entityCount, 1);
    });

    test(
      'a dropped player leaves the others\' view after the linger',
      () async {
        final ana = await join('Ana');
        final bo = await join('Bo');
        await tick();
        await bo.link.close();

        await tick(9);
        expect(ana.client.entityCount, 2);

        await tick(2);
        expect(ana.client.entityCount, 1);
      },
    );
  });

  group('active cells', () {
    test('cells near a player are active and the rest are not', () async {
      await join('Ana');
      await tick();

      expect(server.active.isActiveAt(32, 32), isTrue);
      expect(server.active.isActiveAt(60, 60), isTrue);
      expect(server.active.isActiveAt(100, 100), isFalse);
    });

    test('a cell stays active for a while after the player leaves', () async {
      final ana = await join('Ana');
      await ana.send(Walk(at(100, 32)));

      // At x 66 the player is two cells from the spawn cell.
      await tick(34);
      expect(server.active.isActiveAt(32, 32), isTrue);

      await tick(12);

      expect(server.active.isActiveAt(32, 32), isFalse);
      expect(server.active.isActiveAt(80, 32), isTrue);
    });
  });
}
