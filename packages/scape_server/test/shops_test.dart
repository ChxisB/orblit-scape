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

// The default prices: an axe is worth 30, so it sells for 39 and buys for 12.
const int _axeBuy = 39;
const int _axeSell = 12;

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  List<ShopView> get views => messages.whereType<ShopView>().toList();

  Future<void> trade(int networkId) => send(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.trade),
  );

  Future<void> buy(int item, int count) => send(Buy(item: item, count: count));

  Future<void> sell(int item, int count) =>
      send(Sell(item: item, count: count));
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

  void start({NpcSet? npcs}) {
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
      npcs: npcs ?? defaultNpcs,
      combat: defaultCombat,
      shops: defaultShops,
    );
  }

  PlacedNpc keeper([TileCoord? tile]) => server.npcs.spawn(
    NpcSpawn(npc: NpcId.shopkeeper, tile: tile ?? at(33, 32), radius: 0),
  );

  /// Joins [name] with [coins] and has them open [npc]'s shop.
  Future<TestClient> shopper(
    PlacedNpc npc, {
    String name = 'Ana',
    int coins = 0,
  }) async {
    final player = await join(name);
    if (coins > 0) inventory(name.toLowerCase()).add(ItemId.coins, coins);
    await tick(2);
    await player.trade(npc.networkId);
    await tick(4);
    return player;
  }

  void fillWithBones(Container bag) {
    while (bag.occupied().length < inventorySize) {
      bag.add(ItemId.bones, 1);
    }
  }

  setUp(() => start());

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('opening', () {
    test('trading walks up and sends the stock', () async {
      final ana = await shopper(keeper());

      final lines = ana.views.last.lines;
      expect(ana.views, isNotEmpty);
      expect(lines.map((line) => line.item), contains(ItemId.bronzeAxe));
      expect(
        lines.firstWhere((line) => line.item == ItemId.bronzeAxe).count,
        5,
      );
    });

    test('a line that starts empty is listed at zero', () async {
      final ana = await shopper(keeper());

      final logs = ana.views.last.lines.firstWhere(
        (line) => line.item == ItemId.logs,
      );
      expect(logs.count, 0);
    });

    test('a creature with no shop has nothing to sell', () async {
      final ana = await join('Ana');
      final goblin = server.npcs.spawn(
        NpcSpawn(npc: NpcId.chicken, tile: at(33, 32), radius: 0),
      );
      await tick(2);

      await ana.trade(goblin.networkId);
      await tick(4);

      expect(ana.views, isEmpty);
    });

    test('a request with no window open does nothing', () async {
      final ana = await join('Ana');
      inventory('ana').add(ItemId.coins, 100);

      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(inventory('ana').countOf(ItemId.bronzeAxe), 0);
      expect(inventory('ana').countOf(ItemId.coins), 100);
    });
  });

  group('buying', () {
    test('takes the price and gives the item', () async {
      final ana = await shopper(keeper(), coins: 100);

      await ana.buy(ItemId.bronzeAxe, 2);
      await tick(2);

      expect(inventory('ana').countOf(ItemId.bronzeAxe), 2);
      expect(inventory('ana').countOf(ItemId.coins), 100 - 2 * _axeBuy);
    });

    test('with too few coins changes nothing', () async {
      final ana = await shopper(keeper(), coins: _axeBuy - 1);

      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(ana.notices, contains('You cannot afford that.'));
      expect(inventory('ana').countOf(ItemId.coins), _axeBuy - 1);
      expect(inventory('ana').countOf(ItemId.bronzeAxe), 0);
      expect(ana.views.last.lines.first.count, 5);
    });

    test('with no room changes nothing', () async {
      final ana = await shopper(keeper(), coins: 1000);
      final bag = inventory('ana');
      fillWithBones(bag);

      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(ana.notices, contains('You have no room for that.'));
      expect(bag.countOf(ItemId.coins), 1000);
      expect(bag.countOf(ItemId.bronzeAxe), 0);
    });

    test('is limited by the stock', () async {
      final ana = await shopper(keeper(), coins: 1000);

      await ana.buy(ItemId.bronzeAxe, 6);
      await tick(2);

      expect(ana.notices, contains('They have not got that many.'));
      expect(inventory('ana').countOf(ItemId.coins), 1000);
    });

    test('an item the shop does not stock is refused', () async {
      final ana = await shopper(keeper(), coins: 100);

      await ana.buy(ItemId.bronzeBar, 1);
      await tick(2);

      expect(ana.notices, contains('They do not sell that.'));
      expect(inventory('ana').countOf(ItemId.coins), 100);
    });

    test(
      'stock is shared, so one player buying empties it for another',
      () async {
        final npc = keeper();
        final first = await shopper(npc, coins: 1000);
        final bob = await shopper(npc, name: 'Bob', coins: 1000);

        await first.buy(ItemId.bronzeAxe, 5);
        await tick(2);
        await bob.buy(ItemId.bronzeAxe, 1);
        await tick(2);

        expect(bob.notices, contains('They have not got that many.'));
        expect(inventory('bob').countOf(ItemId.coins), 1000);
      },
    );

    test('tells everyone who is looking', () async {
      final npc = keeper();
      final ana = await shopper(npc, coins: 100);
      final bob = await shopper(npc, name: 'Bob');
      final before = bob.views.length;

      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(bob.views.length, greaterThan(before));
      expect(
        bob.views.last.lines
            .firstWhere((line) => line.item == ItemId.bronzeAxe)
            .count,
        4,
      );
    });
  });

  group('selling', () {
    test('takes the item and pays the price', () async {
      final ana = await shopper(keeper(), coins: 100);
      await ana.buy(ItemId.bronzeAxe, 2);
      await tick(2);

      await ana.sell(ItemId.bronzeAxe, 2);
      await tick(2);

      expect(inventory('ana').countOf(ItemId.bronzeAxe), 0);
      expect(
        inventory('ana').countOf(ItemId.coins),
        100 - 2 * _axeBuy + 2 * _axeSell,
      );
      expect(ana.views.last.lines.first.count, 5);
    });

    test('is capped by the room the shop has', () async {
      final ana = await shopper(keeper(), coins: 100);
      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);
      inventory('ana').add(ItemId.bronzeAxe, 2);

      await ana.sell(ItemId.bronzeAxe, 3);
      await tick(2);

      expect(inventory('ana').countOf(ItemId.bronzeAxe), 2);
      expect(inventory('ana').countOf(ItemId.coins), 100 - _axeBuy + _axeSell);
    });

    test('a full shop takes nothing', () async {
      final ana = await shopper(keeper());
      inventory('ana').add(ItemId.bronzeAxe, 1);

      await ana.sell(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(ana.notices, contains('They will not take any of that.'));
      expect(inventory('ana').countOf(ItemId.bronzeAxe), 1);
      expect(inventory('ana').countOf(ItemId.coins), 0);
    });

    test('an item the shop does not stock is refused', () async {
      final ana = await shopper(keeper());
      inventory('ana').add(ItemId.bronzeBar, 1);

      await ana.sell(ItemId.bronzeBar, 1);
      await tick(2);

      expect(ana.notices, contains('They do not buy that.'));
      expect(inventory('ana').countOf(ItemId.bronzeBar), 1);
    });

    test('with no room for the coins changes nothing', () async {
      start(
        npcs: NpcSet(
          npcs: [
            NpcDef(
              id: 9,
              name: 'Plumer',
              shop: ShopDef(lines: [ShopLine(item: ItemId.feathers, cap: 10)]),
            ),
          ],
        ),
      );
      final npc = server.npcs.spawn(
        NpcSpawn(npc: 9, tile: at(33, 32), radius: 0),
      );
      final ana = await shopper(npc, coins: 40);
      await ana.buy(ItemId.feathers, 5);
      await tick(2);
      final bag = inventory('ana');
      bag.take(bag.search('coins').single);
      fillWithBones(bag);

      await ana.sell(ItemId.feathers, 2);
      await tick(2);

      expect(ana.notices, contains('You have no room for the coins.'));
      expect(bag.countOf(ItemId.feathers), 5);
    });
  });

  group('restocking', () {
    test(
      'brings one item back each interval and never above the cap',
      () async {
        final ana = await shopper(keeper(), coins: 100);
        await ana.buy(ItemId.bronzeAxe, 2);
        await tick(2);
        final line = defaultNpcs.find(NpcId.shopkeeper)!.shop!.lines.first;

        await tick(line.restockTicks * 5);

        expect(ana.views.last.lines.first.count, line.cap);
      },
    );

    test('does not add an item before the interval ends', () async {
      final ana = await shopper(keeper(), coins: 100);
      await ana.buy(ItemId.bronzeAxe, 2);
      await tick(2);
      final line = defaultNpcs.find(NpcId.shopkeeper)!.shop!.lines.first;

      await tick(line.restockTicks - 5);

      expect(ana.views.last.lines.first.count, line.cap - 2);
    });
  });

  group('the window', () {
    test('closes when the player walks away', () async {
      final ana = await shopper(keeper());
      expect(ana.messages.whereType<WindowClosed>(), isEmpty);

      await ana.send(Walk(at(60, 60)));
      await tick(12);

      expect(ana.messages.whereType<WindowClosed>(), isNotEmpty);
    });

    test('keeps a request from working once it has closed', () async {
      final ana = await shopper(keeper(), coins: 100);
      await ana.send(Walk(at(60, 60)));
      await tick(12);

      await ana.buy(ItemId.bronzeAxe, 1);
      await tick(2);

      expect(inventory('ana').countOf(ItemId.bronzeAxe), 0);
    });

    test('a creature with a shop and no rules fails at startup', () async {
      await server.dispose();

      expect(
        () => ScapeServer(
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
        ),
        throwsArgumentError,
      );
      start();
    });
  });
}
