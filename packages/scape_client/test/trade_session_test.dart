import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

const double tickSeconds = 0.6;

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<GameSession> join(String name) async {
    final session = game.connect()..login(name);
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
    return session;
  }

  Future<void> tickAndDraw(GameSession session, [int times = 1]) async {
    for (var i = 0; i < times; i++) {
      game.seconds += tickSeconds;
      await game.tick();
      game.seconds += interpolationDelay.inMilliseconds / 1000;
      session.advance();
    }
  }

  Container bag() => game.server.items.container('ana', ContainerId.inventory)!;

  Future<GameSession> shopper() async {
    final ana = await join('Ana');
    final keeper = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.shopkeeper, tile: TileCoord(33, 32, 0), radius: 0),
    );
    await tickAndDraw(ana, 2);
    ana.trade(keeper.networkId);
    await pump();
    await tickAndDraw(ana, 6);
    return ana;
  }

  test('trading opens the shop and the session holds its stock', () async {
    final ana = await shopper();

    final axe = ana.shop!.lines.firstWhere(
      (line) => line.item == ItemId.bronzeAxe,
    );
    expect(axe.count, 5);
    expect(ana.bankOpen, isFalse);
  });

  test(
    'a purchase changes the inventory and the stock the session sees',
    () async {
      final ana = await shopper();
      bag().add(ItemId.coins, 100);

      ana.buy(ItemId.bronzeAxe, 1);
      await pump();
      await tickAndDraw(ana, 3);

      expect(ana.itemAt(ContainerId.inventory, 1)?.item, ItemId.bronzeAxe);
      expect(ana.shop!.lines.first.count, 4);
    },
  );

  test('a sale pays coins', () async {
    final ana = await shopper();
    bag().add(ItemId.logs, 2);
    await tickAndDraw(ana);

    ana.sell(ItemId.logs, 2);
    await pump();
    await tickAndDraw(ana, 3);

    expect(bag().countOf(ItemId.coins), 2);
    expect(ana.itemAt(ContainerId.inventory, 0)?.item, ItemId.coins);
  });

  test('walking away closes the shop', () async {
    final ana = await shopper();

    ana.walkTo(TileCoord(60, 60, 0));
    await pump();
    await tickAndDraw(ana, 12);

    expect(ana.shop, isNull);
  });

  test('the bank opens, takes a deposit and gives it back', () async {
    final ana = await join('Ana');
    final booth = game.server.objects.place(
      ObjectId.bankBooth,
      TileCoord(33, 32, 0),
    );
    bag().add(ItemId.feathers, 8);
    await tickAndDraw(ana, 2);

    ana.useObject(booth.networkId, 0);
    await pump();
    await tickAndDraw(ana, 6);
    expect(ana.bankOpen, isTrue);

    ana.deposit(0, 8);
    await pump();
    await tickAndDraw(ana, 3);
    expect(ana.itemAt(ContainerId.bank, 0), ItemStack(ItemId.feathers, 8));
    expect(ana.itemAt(ContainerId.inventory, 0), isNull);

    ana.withdraw(0, 3);
    await pump();
    await tickAndDraw(ana, 3);
    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.feathers, 3));
  });

  test('opening a shop closes the bank on the client', () async {
    final ana = await join('Ana');
    final booth = game.server.objects.place(
      ObjectId.bankBooth,
      TileCoord(33, 32, 0),
    );
    final keeper = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.shopkeeper, tile: TileCoord(31, 32, 0), radius: 0),
    );
    await tickAndDraw(ana, 2);
    ana.useObject(booth.networkId, 0);
    await pump();
    await tickAndDraw(ana, 6);
    expect(ana.bankOpen, isTrue);

    ana.trade(keeper.networkId);
    await pump();
    await tickAndDraw(ana, 8);

    expect(ana.shop, isNotNull);
    expect(ana.bankOpen, isFalse);
  });
}
