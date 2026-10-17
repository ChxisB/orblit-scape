import 'dart:math';

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

/// Every try works, every swing lands for one, and every loot roll takes the
/// first entry. A tree that never runs dry gives logs for as long as it is
/// worked.
final class _Lucky implements Random {
  @override
  double nextDouble() => 0;

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => 0;
}

final NodeSet _trees = NodeSet(
  nodes: [
    NodeDef(
      object: ObjectId.tree,
      skill: 'woodcutting',
      level: 1,
      tool: ItemId.bronzeAxe,
      xp: 25,
      product: ItemId.logs,
      chanceLow: 0.3,
      chanceHigh: 0.7,
      fullLevel: 30,
      depleteChance: 0,
      respawnTicks: 25,
    ),
  ],
  objects: defaultObjects,
  items: defaultItems,
  skills: defaultSkills,
);

extension on TestClient {
  List<ShopView> get views => messages.whereType<ShopView>().toList();

  Future<void> click(int kind, int id, {int option = 0}) =>
      send(Interact(kind: kind, id: id, option: option));
}

void main() {
  late ScapeServer server;
  late TestClient ana;

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  /// Steps the world until [done], so a test says what it waits for and not
  /// how many ticks the walking takes.
  Future<void> until(bool Function() done, {int within = 120}) async {
    for (var i = 0; i < within && !done(); i++) {
      await tick();
    }
    expect(done(), isTrue, reason: 'it did not happen within $within ticks');
  }

  Container inventory() =>
      server.items.container('ana', ContainerId.inventory)!;

  Container bank() => server.items.container('ana', ContainerId.bank)!;

  setUp(() async {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: _trees,
      npcs: defaultNpcs,
      story: defaultStory,
      combat: defaultCombat,
      shops: defaultShops,
      random: _Lucky(),
    );
    ana = TestClient(server);
    await ana.login('Ana');
    await tick(2);
  });

  tearDown(() async {
    await ana.dispose();
    await server.dispose();
  });

  // Milestone B: a player can make a living in the world.
  test('one player gathers, fights, loots, sells and banks', () async {
    inventory().add(ItemId.bronzeAxe, 1);
    final tree = server.objects.place(ObjectId.tree, at(33, 32));
    final chicken = server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: at(31, 32), radius: 0),
    );
    final keeper = server.npcs.spawn(
      NpcSpawn(npc: NpcId.shopkeeper, tile: at(32, 36), radius: 0),
    );
    final booth = server.objects.place(ObjectId.bankBooth, at(36, 36));

    await ana.click(InteractKind.object, tree.networkId);
    await until(() => inventory().countOf(ItemId.logs) >= 3);
    expect(server.skills.xpOf('ana', 'woodcutting'), greaterThan(0));

    await ana.click(
      InteractKind.npc,
      chicken.networkId,
      option: NpcOption.attack,
    );
    await until(() => server.ground.lyingAt('ana', chicken.tile).length == 2);
    expect(server.skills.xpOf('ana', 'fighting'), greaterThan(0));
    for (final lying in server.ground.lyingAt('ana', chicken.tile)) {
      final held = inventory().countOf(lying.item);
      await ana.click(InteractKind.ground, lying.networkId);
      await until(() => inventory().countOf(lying.item) > held);
    }
    expect(inventory().countOf(ItemId.bones), 1);
    expect(inventory().countOf(ItemId.feathers), 1);

    await ana.click(
      InteractKind.npc,
      keeper.networkId,
      option: NpcOption.trade,
    );
    await until(() => ana.views.isNotEmpty);
    await ana.send(Sell(item: ItemId.logs, count: 3));
    await tick(2);
    expect(inventory().countOf(ItemId.logs), 0);
    final coins = inventory().countOf(ItemId.coins);
    expect(coins, 3);

    await ana.click(InteractKind.object, booth.networkId);
    await until(() => ana.messages.whereType<BankOpen>().isNotEmpty);
    for (final item in [ItemId.coins, ItemId.feathers]) {
      final slot = inventory().occupied().firstWhere(
        (slot) => inventory().at(slot)!.item == item,
      );
      await ana.send(
        Deposit(slot: slot, count: inventory().at(slot)!.count, tab: 0),
      );
    }
    await tick(2);

    expect(bank().countOf(ItemId.coins), coins);
    expect(bank().countOf(ItemId.feathers), 1);
    expect(inventory().countOf(ItemId.bones), 1);
    expect(inventory().countOf(ItemId.bronzeAxe), 1);
  });
}
