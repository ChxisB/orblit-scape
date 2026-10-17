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

const String _cannotReach = 'You cannot reach that.';
const String _nothingHappens = 'Nothing interesting happens.';

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  int get avatar => messages.whereType<Avatar>().single.networkId;

  Future<void> click(int kind, int id, [int option = 0]) =>
      send(Interact(kind: kind, id: id, option: option));
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

  Container inventory(String key) =>
      server.items.container(key, ContainerId.inventory)!;

  setUp(() {
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
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('a door', () {
    late PlacedObject door;

    setUp(() {
      door = server.objects.place(ObjectId.door, at(40, 32));
      for (var y = 0; y < 128; y++) {
        if (y != 32) map.setFlags(at(40, y), TileFlags.blocked);
      }
    });

    test('is a wall until opened, then the player walks through', () async {
      final ana = await join('Ana');
      expect(findPath(map, spawnTile, at(48, 32)).reached, isFalse);

      await ana.click(InteractKind.object, door.networkId);
      await tick(10);

      expect(door.state, 1);
      expect(ana.tileOf(ana.avatar), at(39, 32));
      expect(findPath(map, at(39, 32), at(48, 32)).reached, isTrue);

      await ana.send(Walk(at(48, 32)));
      await tick(12);

      expect(ana.tileOf(ana.avatar), at(48, 32));
    });

    test('closes again with its second option', () async {
      final ana = await join('Ana');
      server.objects.setState(door, 1);

      await ana.click(InteractKind.object, door.networkId, 1);
      await tick(10);

      expect(door.state, 0);
      expect(ana.notices, isEmpty);
    });

    test('offers only the option that fits its state', () async {
      final ana = await join('Ana');
      server.objects.setState(door, 1);

      await ana.click(InteractKind.object, door.networkId);
      await tick(10);

      expect(door.state, 1);
      expect(ana.notices, [_nothingHappens]);
    });

    test('an option that is not there is ignored', () async {
      final ana = await join('Ana');

      await ana.click(InteractKind.object, door.networkId, 2);
      await ana.click(InteractKind.object, 99999);
      await tick(10);

      expect(door.state, 0);
      expect(ana.notices, isEmpty);
      expect(ana.tileOf(ana.avatar), spawnTile);
    });

    test('a click does nothing when the player is already beside it', () async {
      final ana = await join('Ana');
      await ana.send(Walk(at(39, 32)));
      await tick(8);

      await ana.click(InteractKind.object, door.networkId);
      await tick();

      expect(door.state, 1);
    });
  });

  group('cancelling', () {
    test('a walk before the player arrives means nothing happens', () async {
      final door = server.objects.place(ObjectId.door, at(40, 32));
      final ana = await join('Ana');

      await ana.click(InteractKind.object, door.networkId);
      await tick(2);
      await ana.send(Walk(at(30, 32)));
      await tick(12);

      expect(door.state, 0);
      expect(ana.tileOf(ana.avatar), at(30, 32));
      expect(ana.notices, isEmpty);
    });

    test('a second click replaces the first', () async {
      final first = server.objects.place(ObjectId.door, at(40, 32));
      final second = server.objects.place(ObjectId.door, at(32, 40));
      final ana = await join('Ana');

      await ana.click(InteractKind.object, first.networkId);
      await tick(2);
      await ana.click(InteractKind.object, second.networkId);
      await tick(14);

      expect(first.state, 0);
      expect(second.state, 1);
      expect(ana.notices, isEmpty);
    });
  });

  test('a click while already in reach stops the walk under way', () async {
    final door = server.objects.place(ObjectId.door, at(31, 34));
    final ana = await join('Ana');
    await ana.send(Walk(at(32, 40)));
    await tick(2);
    final stoodAt = ana.tileOf(ana.avatar);

    await ana.click(InteractKind.object, door.networkId);
    await tick(8);

    expect(door.state, 1);
    expect(ana.tileOf(ana.avatar), stoodAt);
  });

  group('unreachable', () {
    test(
      'an object with no way up to it says so and the player stays',
      () async {
        final stairs = server.objects.place(ObjectId.stairsUp, at(50, 50));
        for (var dy = -1; dy <= 1; dy++) {
          for (var dx = -1; dx <= 1; dx++) {
            if (dx != 0 || dy != 0) {
              map.setFlags(at(50 + dx, 50 + dy), TileFlags.blocked);
            }
          }
        }
        final ana = await join('Ana');

        await ana.click(InteractKind.object, stairs.networkId);
        await tick(3);

        expect(ana.notices, [_cannotReach]);
        expect(ana.tileOf(ana.avatar), spawnTile);
      },
    );

    test('an object on another plane cannot be reached', () async {
      final stairs = server.objects.place(
        ObjectId.stairsUp,
        TileCoord(34, 32, 1),
      );
      final ana = await join('Ana');

      await ana.click(InteractKind.object, stairs.networkId);
      await tick(3);

      expect(ana.notices, [_cannotReach]);
    });
  });

  group('stairs', () {
    test('climbing puts the player on the destination plane', () async {
      final stairs = server.objects.place(
        ObjectId.stairsUp,
        at(34, 32),
        destination: TileCoord(36, 36, 1),
      );
      final ana = await join('Ana');

      await ana.click(InteractKind.object, stairs.networkId);
      await tick(6);

      expect(ana.tileOf(ana.avatar), TileCoord(36, 36, 1));

      await tick(3);

      expect(ana.tileOf(ana.avatar), TileCoord(36, 36, 1));
    });

    test('stairs that lead nowhere do nothing', () async {
      final stairs = server.objects.place(ObjectId.stairsDown, at(34, 32));
      final ana = await join('Ana');

      await ana.click(InteractKind.object, stairs.networkId);
      await tick(6);

      expect(ana.notices, [_nothingHappens]);
      expect(ana.tileOf(ana.avatar), at(33, 32));
    });
  });

  group('picking up from the ground', () {
    test('the player walks to the item and takes it', () async {
      final ana = await join('Ana');
      final id = server.ground.drop(ItemId.logs, 1, at: at(36, 32));
      await tick();

      await ana.click(InteractKind.ground, id);
      await tick(8);

      expect(inventory('ana').at(0), ItemStack(ItemId.logs, 1));
      expect(ana.groundItems, isEmpty);
      expect(ana.notices, isEmpty);
    });

    test('a full inventory leaves the item and says so', () async {
      final ana = await join('Ana');
      for (var i = 0; i < inventorySize; i++) {
        inventory('ana').add(ItemId.logs, 1);
      }
      final id = server.ground.drop(ItemId.logs, 1, at: at(34, 32));
      await tick();

      await ana.click(InteractKind.ground, id);
      await tick(6);

      expect(ana.notices, ['You have no room for that.']);
      expect(ana.groundItems.keys, [id]);
    });

    test('an item taken by another before arrival is gone', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await bo.send(Walk(at(34, 32)));
      await tick(3);
      final id = server.ground.drop(ItemId.logs, 1, at: at(34, 32));
      await tick();

      await ana.click(InteractKind.ground, id);
      await tick();
      expect(server.ground.pickUp('bo', id), Outcome.done);
      await tick(5);

      expect(ana.notices, ['It is gone.']);
      expect(inventory('ana').at(0), isNull);
    });

    test('an item the player cannot see is not walked to', () async {
      final ana = await join('Ana');
      await join('Bo');
      final id = server.ground.drop(
        ItemId.logs,
        1,
        at: at(36, 32),
        owner: 'bo',
      );
      await tick();

      await ana.click(InteractKind.ground, id);
      await tick(8);

      expect(ana.tileOf(ana.avatar), spawnTile);
      expect(ana.notices, isEmpty);
      expect(inventory('ana').at(0), isNull);
    });
  });

  group('using an item on an object', () {
    late PlacedObject door;
    final used = <(String, int, int)>[];

    setUp(() {
      used.clear();
      door = server.objects.place(ObjectId.door, at(36, 32));
      server.interactions.addUseOnObject(
        ItemId.logs,
        ObjectId.door,
        (player, slot, object) => used.add((player, slot, object.networkId)),
      );
    });

    test('runs what is registered once the player is beside it', () async {
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.coins, 5)
        ..add(ItemId.logs, 1);

      await ana.click(InteractKind.useOnObject, door.networkId, 1);
      await tick(6);

      expect(used, [('ana', 1, door.networkId)]);
      expect(ana.tileOf(ana.avatar), at(35, 32));
    });

    test('an item with no use on that object does nothing', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.coins, 5);

      await ana.click(InteractKind.useOnObject, door.networkId);
      await tick(6);

      expect(used, isEmpty);
      expect(ana.notices, [_nothingHappens]);
    });

    test('an empty slot is ignored', () async {
      final ana = await join('Ana');

      await ana.click(InteractKind.useOnObject, door.networkId, 5);
      await tick(6);

      expect(used, isEmpty);
      expect(ana.notices, isEmpty);
      expect(ana.tileOf(ana.avatar), spawnTile);
    });

    test('registering the same use twice throws', () {
      expect(
        () => server.interactions.addUseOnObject(
          ItemId.logs,
          ObjectId.door,
          (_, _, _) {},
        ),
        throwsArgumentError,
      );
    });
  });

  group('using an item on an item', () {
    final used = <(String, int, int)>[];

    setUp(() {
      used.clear();
      server.interactions.addUseOnItem(
        ItemId.logs,
        ItemId.coins,
        (player, slot, onSlot) => used.add((player, slot, onSlot)),
      );
    });

    Future<TestClient> anaWithLogsThenCoins() async {
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.logs, 1)
        ..add(ItemId.coins, 5);
      return ana;
    }

    test('runs at once with the first item in the first slot', () async {
      final ana = await anaWithLogsThenCoins();

      await ana.click(InteractKind.useOnItem, 1, 0);
      await tick();

      expect(used, [('ana', 0, 1)]);
      expect(ana.tileOf(ana.avatar), spawnTile);
    });

    test('works whichever item is used on the other', () async {
      final ana = await anaWithLogsThenCoins();

      await ana.click(InteractKind.useOnItem, 0, 1);
      await tick();

      expect(used, [('ana', 0, 1)]);
    });

    test('an item on itself or an unmatched pair does nothing', () async {
      final ana = await anaWithLogsThenCoins();
      inventory('ana').add(ItemId.bronzeHelmet, 1);

      await ana.click(InteractKind.useOnItem, 0, 0);
      await tick();
      await ana.click(InteractKind.useOnItem, 2, 0);
      await tick();

      expect(used, isEmpty);
      expect(ana.notices, [_nothingHappens, _nothingHappens]);
    });

    test('an item is never used on its own slot', () async {
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.logs, 1)
        ..add(ItemId.logs, 1);
      server.interactions.addUseOnItem(
        ItemId.logs,
        ItemId.logs,
        (player, slot, onSlot) => used.add((player, slot, onSlot)),
      );

      await ana.click(InteractKind.useOnItem, 0, 0);
      await tick();
      expect(used, isEmpty);

      await ana.click(InteractKind.useOnItem, 1, 0);
      await tick();
      expect(used, [('ana', 0, 1)]);
    });

    test('a stale slot does nothing', () async {
      final ana = await anaWithLogsThenCoins();

      await ana.click(InteractKind.useOnItem, 9, 0);
      await tick();

      expect(used, isEmpty);
      expect(ana.notices, [_nothingHappens]);
    });

    test('registering the pair again in either order throws', () {
      for (final (a, b) in [
        (ItemId.logs, ItemId.coins),
        (ItemId.coins, ItemId.logs),
      ]) {
        expect(
          () => server.interactions.addUseOnItem(a, b, (_, _, _) {}),
          throwsArgumentError,
        );
      }
    });
  });

  test('an action name can be registered once', () {
    expect(
      () => server.interactions.addObjectAction('toggle', (_, _) {}),
      throwsArgumentError,
    );
  });
}
