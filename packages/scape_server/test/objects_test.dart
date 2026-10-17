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

  group('placing', () {
    test('a placed object is seen with its definition and state', () async {
      final ana = await join('Ana');
      final door = server.objects.place(ObjectId.door, at(36, 32));

      await tick();

      final stairs = server.objects.place(ObjectId.stairsDown, at(38, 32));
      await tick();

      expect(ana.objects, {
        door.networkId: (ObjectId.door, 0),
        stairs.networkId: (ObjectId.stairsDown, 0),
      });
      expect(ana.tileOf(door.networkId), at(36, 32));
      expect(server.objects.find(door.networkId), same(door));
      expect(server.objects.at(at(36, 32)), same(door));
      expect(server.objects.at(at(37, 32)), isNull);
    });

    test('an object is not an actor and the player is not an object', () async {
      final ana = await join('Ana');
      server.objects.place(ObjectId.door, at(36, 32));
      await tick();

      expect(ana.client.entityCount, 2);
      expect(ana.objects, hasLength(1));
    });

    test('a solid object blocks its tile and a stair does not', () {
      server.objects.place(ObjectId.door, at(36, 32));
      server.objects.place(ObjectId.stairsUp, at(38, 32));

      expect(map.flagsAt(at(36, 32)).has(TileFlags.blocked), isTrue);
      expect(map.flagsAt(at(38, 32)).has(TileFlags.blocked), isFalse);
    });

    test('a tile that is blocked, water or taken refuses an object', () {
      map.setFlags(at(40, 40), TileFlags.blocked);
      map.setFlags(at(42, 40), TileFlags.water);
      server.objects.place(ObjectId.stairsUp, at(44, 40));

      for (final tile in [at(40, 40), at(42, 40), at(44, 40)]) {
        expect(
          () => server.objects.place(ObjectId.stairsDown, tile),
          throwsArgumentError,
        );
      }
    });

    test('an object that does not exist cannot be placed', () {
      expect(() => server.objects.place(999, at(36, 32)), throwsArgumentError);
    });
  });

  group('state', () {
    test('a door that opens frees its tile and the client is told', () async {
      final ana = await join('Ana');
      final door = server.objects.place(ObjectId.door, at(36, 32));
      await tick();

      server.objects.setState(door, 1);
      await tick();

      expect(door.state, 1);
      expect(map.flagsAt(at(36, 32)).has(TileFlags.blocked), isFalse);
      expect(ana.objects[door.networkId], (ObjectId.door, 1));

      server.objects.setState(door, 0);
      await tick();

      expect(map.flagsAt(at(36, 32)).has(TileFlags.blocked), isTrue);
      expect(ana.objects[door.networkId], (ObjectId.door, 0));
    });

    test('a state outside the int32 range is refused', () {
      final door = server.objects.place(ObjectId.door, at(36, 32));

      expect(() => server.objects.setState(door, -1), throwsRangeError);
      expect(() => server.objects.setState(door, 0x80000000), throwsRangeError);
      server.objects.setState(door, 0x7FFFFFFF);
      expect(door.state, 0x7FFFFFFF);
    });

    test('opening a door leaves a wall flag beside it alone', () {
      map.setFlags(at(36, 32), TileFlags.wallNorth);
      final door = server.objects.place(ObjectId.door, at(36, 32));
      server.objects.setState(door, 1);

      expect(map.flagsAt(at(36, 32)).has(TileFlags.wallNorth), isTrue);
    });
  });
}
