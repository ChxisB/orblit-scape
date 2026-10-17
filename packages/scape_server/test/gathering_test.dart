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

/// Dice that answer from a list, over and over.
final class _Rolls implements Random {
  _Rolls(this._values);

  final List<double> _values;
  int drawn = 0;

  @override
  double nextDouble() => _values[drawn++ % _values.length];

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => throw UnimplementedError();
}

/// A try that succeeds, then a node that stays full.
const List<double> _gives = [0.0, 0.99];

/// A try that succeeds and then runs the node dry.
const List<double> _givesAndDries = [0.0, 0.0];

/// A try that finds nothing.
const List<double> _misses = [0.99];

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  Future<void> click(int id) =>
      send(Interact(kind: InteractKind.object, id: id, option: 0));
}

NodeDef _tree({int level = 1, int respawnTicks = 25}) => NodeDef(
  object: ObjectId.tree,
  skill: 'woodcutting',
  level: level,
  tool: ItemId.bronzeAxe,
  xp: 25,
  product: ItemId.logs,
  chanceLow: 0.3,
  chanceHigh: 0.7,
  fullLevel: 30,
  depleteChance: 0.15,
  respawnTicks: respawnTicks,
);

NodeSet _only(NodeDef node) => NodeSet(
  nodes: [node],
  objects: defaultObjects,
  items: defaultItems,
  skills: defaultSkills,
);

void main() {
  late ScapeServer server;
  late _Rolls rolls;
  late PlacedObject tree;
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

  Future<void> start(List<double> dice, {NodeSet? nodes}) async {
    rolls = _Rolls(dice);
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: nodes ?? defaultNodes,
      npcs: defaultNpcs,
      shops: defaultShops,
      combat: defaultCombat,
      random: rolls,
    );
    tree = server.objects.place(ObjectId.tree, at(33, 32));
  }

  /// Joins a player holding an axe and starts them chopping.
  Future<TestClient> chopper([String name = 'Ana']) async {
    final player = await join(name);
    inventory(name.toLowerCase()).add(ItemId.bronzeAxe, 1);
    await player.click(tree.networkId);
    return player;
  }

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('a try', () {
    test('that rolls under the chance gives the product and xp', () async {
      await start(_gives);
      await chopper();

      await tick(gatherTicks + 1);

      expect(inventory('ana').countOf(ItemId.logs), 1);
      expect(server.skills.xpOf('ana', 'woodcutting'), 25);
    });

    test('waits the gather delay before each try', () async {
      await start(_gives);
      await chopper();

      await tick(gatherTicks);
      expect(inventory('ana').countOf(ItemId.logs), 0);

      await tick();
      expect(inventory('ana').countOf(ItemId.logs), 1);

      await tick(gatherTicks);
      expect(inventory('ana').countOf(ItemId.logs), 2);
    });

    test('that rolls over the chance gives nothing and tries again', () async {
      await start([..._misses, ..._misses, ..._gives]);
      await chopper();

      await tick(gatherTicks * 2 + 1);
      expect(inventory('ana').countOf(ItemId.logs), 0);
      expect(server.skills.xpOf('ana', 'woodcutting'), 0);

      await tick(gatherTicks);
      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('is judged against the chance at the player level', () async {
      await start([0.5]);
      final ana = await chopper();
      await tick(gatherTicks + 1);
      expect(inventory('ana').countOf(ItemId.logs), 0);

      server.skills.grant(
        'ana',
        'woodcutting',
        defaultSkills.curve.xpForLevel(30),
      );
      await ana.click(tree.networkId);
      await tick(gatherTicks + 1);

      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('at the chance itself does not succeed', () async {
      await start([0.3]);
      await chopper();

      await tick(gatherTicks + 1);

      expect(inventory('ana').countOf(ItemId.logs), 0);
    });
  });

  group('experience', () {
    test('a level gained is told once', () async {
      await start(_gives);
      final ana = await chopper();
      server.skills.grant(
        'ana',
        'woodcutting',
        defaultSkills.curve.xpForLevel(2) - 10,
      );

      await tick(gatherTicks + 1);

      expect(ana.notices, ['You reached level 2 in woodcutting.']);
    });
  });

  group('a refusal', () {
    test('without the tool says which tool', () async {
      await start(_gives);
      final ana = await join('Ana');

      await ana.click(tree.networkId);
      await tick(gatherTicks * 2);

      expect(ana.notices, ['You need a bronze axe to do that.']);
      expect(rolls.drawn, 0);
    });

    test('with the tool worn is not given', () async {
      await start(_gives);
      final ana = await join('Ana');
      server.items
          .container('ana', ContainerId.equipment)!
          .add(ItemId.bronzeAxe, 1);

      await ana.click(tree.networkId);
      await tick(gatherTicks + 1);

      expect(ana.notices, isEmpty);
      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('one level short says which level', () async {
      await start(_gives, nodes: _only(_tree(level: 5)));
      final ana = await chopper();
      server.skills.grant(
        'ana',
        'woodcutting',
        defaultSkills.curve.xpForLevel(4),
      );

      await tick(gatherTicks * 2);

      expect(ana.notices, ['You need level 5 in woodcutting to do that.']);
      expect(rolls.drawn, 0);
    });

    test('at the level needed gathers', () async {
      await start(_gives, nodes: _only(_tree(level: 5)));
      final ana = await join('Ana');
      inventory('ana').add(ItemId.bronzeAxe, 1);
      server.skills.grant(
        'ana',
        'woodcutting',
        defaultSkills.curve.xpForLevel(5),
      );

      await ana.click(tree.networkId);
      await tick(gatherTicks + 1);

      expect(ana.notices, isEmpty);
      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('with a full bag is told and gives nothing', () async {
      await start(_gives);
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.bronzeAxe, 1)
        ..add(ItemId.logs, inventorySize - 1);

      await ana.click(tree.networkId);
      await tick(gatherTicks * 2);

      expect(ana.notices, ['Your inventory is full.']);
      expect(inventory('ana').countOf(ItemId.logs), inventorySize - 1);
    });
  });

  group('the loop', () {
    test('stops when the bag fills', () async {
      await start(_gives);
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.bronzeAxe, 1)
        ..add(ItemId.logs, inventorySize - 2);

      await ana.click(tree.networkId);
      await tick(gatherTicks * 8);

      expect(inventory('ana').countOf(ItemId.logs), inventorySize - 1);
      expect(ana.notices, ['Your inventory is full.']);
    });

    test('stops when the player walks away', () async {
      await start(_gives);
      final ana = await chopper();
      await tick(gatherTicks + 1);
      expect(inventory('ana').countOf(ItemId.logs), 1);

      await ana.send(Walk(at(40, 40)));
      await tick(gatherTicks * 4);

      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('stops when the player leaves', () async {
      await start(_gives);
      await chopper();
      await tick(gatherTicks + 1);
      final before = rolls.drawn;

      await server.host.removeClient('ana');
      await tick(gatherTicks * 3);

      expect(rolls.drawn, before);
    });
  });

  group('a node that runs dry', () {
    test('turns bare and stops the loop', () async {
      await start(_givesAndDries);
      final ana = await chopper();

      await tick(gatherTicks * 3);

      expect(tree.state, nodeDepleted);
      expect(inventory('ana').countOf(ItemId.logs), 1);
      expect(ana.notices, isEmpty);
    });

    test('tells a second player who finds it dry', () async {
      await start(_givesAndDries);
      await chopper();
      final bob = await chopper('Bob');

      await tick(gatherTicks * 3);

      expect(bob.notices, ['There is nothing left.']);
      expect(inventory('bob').countOf(ItemId.logs), 0);
    });

    test('fills again on its respawn tick', () async {
      await start(_givesAndDries);
      await chopper();
      var ticks = 0;
      while (tree.state == nodeFull) {
        await tick();
        ticks++;
        expect(ticks, lessThan(20));
      }

      await tick(_tree().respawnTicks - 1);
      expect(tree.state, nodeDepleted);

      await tick();
      expect(tree.state, nodeFull);
    });

    test('is not refilled once it has filled and is dried by hand', () async {
      await start(_givesAndDries);
      await chopper();
      while (tree.state == nodeFull) {
        await tick();
      }
      await tick(_tree().respawnTicks);
      expect(tree.state, nodeFull);

      server.objects.setState(tree, nodeDepleted);
      await tick(3);

      expect(tree.state, nodeDepleted);
    });

    test('waits for a player to be near before it fills', () async {
      await start(_givesAndDries, nodes: _only(_tree(respawnTicks: 80)));
      final ana = await chopper();
      await tick(gatherTicks + 1);
      expect(tree.state, nodeDepleted);

      await ana.send(Walk(at(100, 32)));
      await tick(120);
      expect(server.active.isActiveAt(33, 32), isFalse);
      expect(tree.state, nodeDepleted);

      await ana.send(Walk(at(36, 32)));
      var ticks = 0;
      while (!server.active.isActiveAt(33, 32)) {
        await tick();
        ticks++;
        expect(ticks, lessThan(120));
      }

      expect(tree.state, nodeFull);
    });
  });

  group('other nodes', () {
    final cases = [
      (ObjectId.rock, ItemId.bronzePickaxe, ItemId.copperOre, 'mining'),
      (ObjectId.fishingSpot, ItemId.fishingNet, ItemId.rawFish, 'fishing'),
    ];

    for (final (object, tool, product, skill) in cases) {
      test('a $skill try gives its own product and xp', () async {
        await start(_gives);
        final node = server.objects.place(object, at(32, 33));
        final ana = await join('Ana');
        inventory('ana').add(tool, 1);

        await ana.click(node.networkId);
        await tick(gatherTicks + 1);

        expect(inventory('ana').countOf(product), 1);
        expect(server.skills.xpOf('ana', skill), greaterThan(0));
      });
    }
  });
}
