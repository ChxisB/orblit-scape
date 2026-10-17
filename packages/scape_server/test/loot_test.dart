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

/// Dice for a one-hit kill. Every swing lands. [low] picks what every integer
/// roll answers: zero takes the first entry and the rare table, and the top
/// of the range takes the last entry and skips the rare table.
final class _Dice implements Random {
  _Dice({required this.low});

  final bool low;

  @override
  double nextDouble() => 0;

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => low ? 0 : max - 1;
}

const int _rat = 1;

NpcSet _rats(LootTable? loot) => NpcSet(
  npcs: [NpcDef(id: _rat, name: 'Rat', fight: NpcFight(health: 1), loot: loot)],
);

LootTable _table() => LootTable(
  always: [ItemStack(ItemId.bones, 1)],
  entries: [
    LootItem(weight: 1, item: ItemId.coins, min: 5, max: 9),
    LootItem(weight: 1, item: ItemId.feathers),
  ],
  rare: LootTable(always: [ItemStack(ItemId.bronzeSword, 1)]),
  rareOneIn: 25,
);

extension on TestClient {
  Future<void> attack(int networkId) => send(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.attack),
  );
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

  void start({LootTable? loot, bool low = true, NpcSet? npcs}) {
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
      npcs: npcs ?? _rats(loot),
      combat: defaultCombat,
      random: _Dice(low: low),
    );
  }

  /// What [player] sees lying on [tile], as item to count.
  Map<int, int> seenBy(String player, TileCoord tile) => {
    for (final lying in server.ground.lyingAt(player, tile))
      lying.item: lying.count,
  };

  /// Joins Ana and Bo, has Ana kill a rat beside her and returns the tile it
  /// died on.
  Future<TileCoord> kill() async {
    final ana = await join('Ana');
    await join('Bo');
    final rat = server.npcs.spawn(
      NpcSpawn(npc: _rat, tile: at(33, 32), radius: 0),
    );
    await tick(2);
    await ana.attack(rat.networkId);
    await tick(6);
    return rat.tile;
  }

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('a kill', () {
    test('drops what the table pays where the creature died', () async {
      start(loot: _table());

      final tile = await kill();

      expect(seenBy('ana', tile), {
        ItemId.bones: 1,
        ItemId.bronzeSword: 1,
        ItemId.coins: 5,
      });
    });

    test('leaves out the rare table when its die does not come up', () async {
      start(loot: _table(), low: false);

      final tile = await kill();

      expect(seenBy('ana', tile), {ItemId.bones: 1, ItemId.feathers: 1});
    });

    test('is for the killer first', () async {
      start(loot: _table());

      final tile = await kill();

      expect(seenBy('bo', tile), isEmpty);
    });

    test('is for everyone once the owner time ends', () async {
      start(loot: _table());
      final tile = await kill();

      await tick(groundOwnerTicks + 1);

      expect(seenBy('bo', tile), isNotEmpty);
    });

    test('from a creature with no table drops nothing', () async {
      start();

      final tile = await kill();

      expect(seenBy('ana', tile), isEmpty);
    });
  });

  test('a table that names an item that is not there fails at startup', () {
    // The first server is the one tearDown closes. The second never exists.
    start();
    expect(
      () => start(loot: LootTable(always: [ItemStack(9999, 1)])),
      throwsArgumentError,
    );
  });
}
