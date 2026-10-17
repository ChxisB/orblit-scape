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
  late TileMap map;
  final clients = <TestClient>[];

  TestClient connect() {
    final client = TestClient(server);
    clients.add(client);
    return client;
  }

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  Future<(TestClient, int)> join(String name) async {
    final client = connect();
    await client.login(name);
    await tick();
    final avatar = client.messages.whereType<Avatar>().single.networkId;
    return (client, avatar);
  }

  setUp(() {
    map = openWorld();
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
      npcs: defaultNpcs,
      story: defaultStory,
      shops: defaultShops,
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

  test(
    'a player appears on the spawn tile and is told which is theirs',
    () async {
      final (ana, avatar) = await join('Ana');

      expect(ana.tileOf(avatar), spawnTile);
      expect(ana.client.entityCount, 1);
    },
  );

  test('a walk takes one step each tick and ends on the target', () async {
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(at(37, 32)));
    final seen = <TileCoord?>[];
    for (var i = 0; i < 6; i++) {
      await tick();
      seen.add(ana.tileOf(avatar));
    }

    expect(seen, [
      at(33, 32),
      at(34, 32),
      at(35, 32),
      at(36, 32),
      at(37, 32),
      at(37, 32),
    ]);
  });

  test('a walk goes round a blocked tile and never stands on it', () async {
    map.setFlags(at(33, 32), TileFlags.blocked);
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(at(34, 32)));
    final seen = <TileCoord?>[];
    for (var i = 0; i < 4; i++) {
      await tick();
      seen.add(ana.tileOf(avatar));
    }

    // A diagonal past a blocked corner is refused, so the detour is four
    // steps and not two. Which side it takes round is the search's choice.
    expect(seen.last, at(34, 32));
    expect(seen.take(3).map((t) => t!.y), everyElement(isNot(32)));
  });

  test('a new walk replaces the one under way', () async {
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(at(40, 32)));
    await tick(2);
    expect(ana.tileOf(avatar), at(34, 32));

    await ana.send(Walk(at(34, 36)));
    await tick(4);

    expect(ana.tileOf(avatar), at(34, 36));
  });

  test('a target that cannot be reached ends the walk beside it', () async {
    map.setFlags(at(36, 32), TileFlags.blocked);
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(at(36, 32)));
    await tick(6);

    expect(ana.tileOf(avatar)!.x, inInclusiveRange(34, 35));
  });

  test('a walk to another plane is ignored', () async {
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(TileCoord(40, 32, 1)));
    await tick(3);

    expect(ana.tileOf(avatar), spawnTile);
  });

  test('chat and interactions do not move a walking player', () async {
    final (ana, avatar) = await join('Ana');

    await ana.send(Walk(at(40, 32)));
    await tick();
    await ana.send(Chat('hello'));
    await tick();

    expect(ana.tileOf(avatar), at(34, 32));
  });

  test('two players walk without disturbing each other', () async {
    final (ana, anaId) = await join('Ana');
    final (bo, boId) = await join('Bo');

    await ana.send(Walk(at(36, 32)));
    await bo.send(Walk(at(32, 36)));
    await tick(4);

    expect(ana.tileOf(anaId), at(36, 32));
    expect(bo.tileOf(boId), at(32, 36));
  });

  test('a player who is gone leaves the world after the linger time', () async {
    final (ana, avatar) = await join('Ana');
    await join('Bo');
    await ana.link.close();

    await tick(11);

    expect(server.host.entityFor(avatar), isNull);
  });

  test('an avatar names its player only while the player is in', () async {
    final (ana, avatar) = await join('Ana');
    await join('Bo');
    expect(server.movement.playerWith(avatar), 'ana');
    await ana.link.close();

    await tick(11);

    expect(server.movement.playerWith(avatar), isNull);
  });

  test('a spawn tile that is not open ground is refused', () {
    map.setFlags(spawnTile, TileFlags.blocked);
    expect(
      () => ScapeServer(
        clock: TickClock(period: tickPeriod, now: FakeTime().read),
        pacer: NoPacer(),
        log: (_) {},
        map: map,
        spawn: spawnTile,
        skills: defaultSkills,
        items: defaultItems,
        objects: defaultObjects,
        nodes: defaultNodes,
        npcs: defaultNpcs,
        story: defaultStory,
        shops: defaultShops,
        combat: defaultCombat,
      ),
      throwsArgumentError,
    );
  });
}
