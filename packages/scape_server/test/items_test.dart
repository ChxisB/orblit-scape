import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

extension on TestClient {
  /// The container changes this player was sent, oldest first.
  List<SlotsDelta> get itemDeltas => [
    for (final PrivateState(:delta) in messages.whereType<PrivateState>())
      if (delta.first == itemsDeltaTag) tryDecodeSlots(delta, defaultItems)!,
  ];
}

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

  Container inventory(String key) =>
      server.items.container(key, ContainerId.inventory)!;

  setUp(() {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('containers', () {
    test('a new player has an empty inventory, equipment and bank', () async {
      final ana = await join('Ana');

      for (final id in ContainerId.values) {
        final container = server.items.container('ana', id)!;
        expect(container.dirty, isEmpty);
      }
      expect(ana.itemDeltas, isEmpty);
    });

    test('each container has its own shape', () async {
      await join('Ana');
      Container of(ContainerId id) => server.items.container('ana', id)!;

      expect(of(ContainerId.inventory).size, inventorySize);
      expect(of(ContainerId.bank).size, bankTabs * bankTabSize);
      expect(of(ContainerId.equipment).size, equipmentSlots.length);
      expect(of(ContainerId.equipment).add(ItemId.logs, 1), 0);
      expect(of(ContainerId.equipment).add(ItemId.bronzeHelmet, 1), 1);
    });

    test(
      'a change is sent as one delta naming the slots that changed',
      () async {
        final ana = await join('Ana');
        inventory('ana')
          ..add(ItemId.coins, 50)
          ..add(ItemId.logs, 1);

        await tick();

        expect(ana.itemDeltas, hasLength(1));
        expect(ana.itemDeltas.single.container, ContainerId.inventory);
        expect(ana.itemDeltas.single.changes, [
          (slot: 0, stack: ItemStack(ItemId.coins, 50)),
          (slot: 1, stack: ItemStack(ItemId.logs, 1)),
        ]);
      },
    );

    test('a tick with no change sends nothing', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.coins, 5);
      await tick();

      await tick(3);

      expect(ana.itemDeltas, hasLength(1));
    });

    test('a slot changed twice in a tick is sent once, as it ends', () async {
      final ana = await join('Ana');
      final bag = inventory('ana')..add(ItemId.coins, 5);
      bag.add(ItemId.coins, 5);

      await tick();

      expect(ana.itemDeltas.single.changes, [
        (slot: 0, stack: ItemStack(ItemId.coins, 10)),
      ]);
    });

    test('a change to the bank names the bank', () async {
      final ana = await join('Ana');
      server.items.container('ana', ContainerId.bank)!.add(ItemId.logs, 1);

      await tick();

      expect(ana.itemDeltas.single.container, ContainerId.bank);
    });

    test('only the owner is told', () async {
      await join('Ana');
      final bo = await join('Bo');
      inventory('ana').add(ItemId.coins, 5);

      await tick();

      expect(bo.itemDeltas, isEmpty);
    });

    test('a player who leaves has no containers', () async {
      final ana = await join('Ana');
      await ana.link.close();

      await tick(12);

      expect(server.items.container('ana', ContainerId.inventory), isNull);
    });

    test('a player who is not in the world has none', () {
      expect(server.items.container('nobody', ContainerId.bank), isNull);
    });
  });

  group('ground items', () {
    Future<int> dropFromAna(TestClient ana, {int? count}) async {
      inventory('ana').add(ItemId.coins, 10);
      await tick();
      expect(server.ground.dropFrom('ana', 0, count: count), Outcome.done);
      await tick();
      return ana.groundItems.keys.single;
    }

    test('an item dropped is seen by its owner and not by others', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');

      final id = await dropFromAna(ana);

      expect(ana.groundItems, {id: (ItemId.coins, 10)});
      expect(bo.groundItems, isEmpty);
      expect(ana.tileOf(id), spawnTile);
    });

    test('dropping takes the item from the inventory', () async {
      final ana = await join('Ana');

      await dropFromAna(ana, count: 4);

      expect(inventory('ana').at(0), ItemStack(ItemId.coins, 6));
      expect(ana.groundItems.values.single, (ItemId.coins, 4));
    });

    test('is hidden from others until the owner timer ends', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final id = await dropFromAna(ana);

      await tick(groundOwnerTicks - 2);
      expect(bo.groundItems, isEmpty);

      await tick();
      expect(bo.groundItems, {id: (ItemId.coins, 10)});
      expect(ana.groundItems, {id: (ItemId.coins, 10)});
    });

    test('vanishes for everyone after the public time', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await dropFromAna(ana);
      await tick(groundOwnerTicks - 1);
      expect(bo.groundItems, hasLength(1));

      await tick(groundPublicTicks - 1);
      expect(ana.groundItems, hasLength(1));
      expect(bo.groundItems, hasLength(1));

      await tick();
      expect(ana.groundItems, isEmpty);
      expect(bo.groundItems, isEmpty);
      expect(server.ground.lyingAt('ana', spawnTile), isEmpty);
    });

    test('an item with no owner is open to all at once', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');

      final id = server.ground.drop(ItemId.logs, 1, at: spawnTile);
      await tick();

      expect(ana.groundItems, {id: (ItemId.logs, 1)});
      expect(bo.groundItems, {id: (ItemId.logs, 1)});
    });

    test('an item with no owner lies for the public time only', () async {
      final ana = await join('Ana');
      server.ground.drop(ItemId.logs, 1, at: spawnTile);
      await tick();

      await tick(groundPublicTicks - 1);

      expect(ana.groundItems, isEmpty);
    });

    test('the owner leaving does not end the claim', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await dropFromAna(ana);
      await ana.link.close();

      await tick(groundOwnerTicks - 3);

      expect(bo.groundItems, isEmpty);
    });

    test('lyingAt lists what a player can see on a tile', () async {
      final ana = await join('Ana');
      await join('Bo');
      final id = await dropFromAna(ana);

      expect(server.ground.lyingAt('ana', spawnTile), [
        (networkId: id, item: ItemId.coins, count: 10),
      ]);
      expect(server.ground.lyingAt('bo', spawnTile), isEmpty);
      expect(server.ground.lyingAt('ana', TileCoord(1, 1, 0)), isEmpty);
    });

    test('refuses an item that does not exist and a count of none', () {
      expect(
        () => server.ground.drop(9999, 1, at: spawnTile),
        throwsArgumentError,
      );
      expect(
        () => server.ground.drop(ItemId.logs, 0, at: spawnTile),
        throwsArgumentError,
      );
    });

    test(
      'dropping from an empty slot or a player not here is nothing',
      () async {
        await join('Ana');

        expect(server.ground.dropFrom('ana', 0), Outcome.nothingThere);
        expect(server.ground.dropFrom('ana', 99), Outcome.nothingThere);
        expect(server.ground.dropFrom('nobody', 0), Outcome.nothingThere);
      },
    );

    test('a stack at the integer limit can be dropped whole', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.coins, maxStack);
      await tick();

      expect(server.ground.dropFrom('ana', 0), Outcome.done);
      await tick();

      expect(ana.groundItems.values.single, (ItemId.coins, maxStack));
    });
  });

  group('picking up', () {
    test('the owner on the tile takes the item into the inventory', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.coins, 10);
      await tick();
      server.ground.dropFrom('ana', 0);
      await tick();
      final id = ana.groundItems.keys.single;

      expect(server.ground.pickUp('ana', id), Outcome.done);
      await tick();

      expect(inventory('ana').at(0), ItemStack(ItemId.coins, 10));
      expect(ana.groundItems, isEmpty);
      expect(server.ground.lyingAt('ana', spawnTile), isEmpty);
    });

    test('another player cannot take an item they cannot see', () async {
      final ana = await join('Ana');
      await join('Bo');
      final id = server.ground.drop(
        ItemId.logs,
        1,
        at: spawnTile,
        owner: 'ana',
      );
      await tick();

      expect(server.ground.pickUp('bo', id), Outcome.nothingThere);
      expect(inventory('bo').at(0), isNull);
      expect(ana.groundItems.keys, [id]);
    });

    test('another player may take it once it is public', () async {
      final ana = await join('Ana');
      await join('Bo');
      final id = server.ground.drop(
        ItemId.logs,
        1,
        at: spawnTile,
        owner: 'ana',
      );
      await tick(groundOwnerTicks);

      expect(server.ground.pickUp('bo', id), Outcome.done);
      await tick();

      expect(inventory('bo').at(0), ItemStack(ItemId.logs, 1));
      expect(ana.groundItems, isEmpty);
    });

    test('a player not on the tile cannot reach it', () async {
      await join('Ana');
      final id = server.ground.drop(ItemId.logs, 1, at: TileCoord(40, 40, 0));

      expect(server.ground.pickUp('ana', id), Outcome.notAllowed);
      expect(server.ground.lyingAt('ana', TileCoord(40, 40, 0)), hasLength(1));
    });

    test(
      'an item that is gone, and a player who is not here, give nothing',
      () async {
        await join('Ana');
        final id = server.ground.drop(ItemId.logs, 1, at: spawnTile);

        expect(server.ground.pickUp('ana', id + 100), Outcome.nothingThere);
        expect(server.ground.pickUp('nobody', id), Outcome.nothingThere);
      },
    );

    test('a full inventory takes nothing and the item stays', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.logs, inventorySize);
      final id = server.ground.drop(ItemId.logs, 1, at: spawnTile);

      expect(server.ground.pickUp('ana', id), Outcome.wontFit);
      await tick();

      expect(ana.groundItems.keys, [id]);
    });

    test('room for part takes that part and updates what is left', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.logs, inventorySize - 1);
      inventory('ana').add(ItemId.coins, maxStack - 3);
      final id = server.ground.drop(ItemId.coins, 10, at: spawnTile);
      await tick();

      expect(server.ground.pickUp('ana', id), Outcome.partly);
      await tick();

      expect(inventory('ana').countOf(ItemId.coins), maxStack);
      expect(ana.groundItems, {id: (ItemId.coins, 7)});
    });
  });
}
