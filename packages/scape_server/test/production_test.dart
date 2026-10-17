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

/// A make that works at any level the recipes set.
const List<double> _works = [0.99];

/// A make that fails whenever it can.
const List<double> _fails = [0.0];

const int _makeTicks = 3;

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  Future<void> useOnFire(int slot, int networkId) => send(
    Interact(kind: InteractKind.useOnObject, id: networkId, option: slot),
  );

  Future<void> useOnItem(int slot, int onSlot) =>
      send(Interact(kind: InteractKind.useOnItem, id: onSlot, option: slot));
}

RecipeSet _only(Recipe recipe) => RecipeSet(
  recipes: [recipe],
  objects: defaultObjects,
  items: defaultItems,
  skills: defaultSkills,
);

Recipe _cook({int level = 1, int inputs = 1}) => Recipe(
  name: 'cook the fish',
  skill: 'cooking',
  level: level,
  xp: 30,
  start: const OnObject(ItemId.rawFish, ObjectId.fire),
  inputs: [ItemStack(ItemId.rawFish, inputs)],
  product: Gives(ItemStack(ItemId.cookedFish, 1)),
  failure: Failure(chance: 0.5, safeLevel: level + 29, item: ItemId.burntFish),
);

void main() {
  late ScapeServer server;
  late _Rolls rolls;
  late PlacedObject fire;
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

  Future<void> start(List<double> dice, {RecipeSet? recipes}) async {
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
      nodes: defaultNodes,
      npcs: defaultNpcs,
      story: defaultStory,
      shops: defaultShops,
      combat: defaultCombat,
      recipes: recipes ?? defaultRecipes,
      random: rolls,
    );
    fire = server.objects.place(ObjectId.fire, at(33, 32));
  }

  void level(String player, String skill, int level) =>
      server.skills.grant(player, skill, defaultSkills.curve.xpForLevel(level));

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('cooking', () {
    test('uses the fish and gives a cooked one after the delay', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks);
      expect(inventory('ana').countOf(ItemId.cookedFish), 0);

      await tick();
      expect(inventory('ana').countOf(ItemId.rawFish), 0);
      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
      expect(server.skills.xpOf('ana', 'cooking'), 30);
    });

    test('keeps going until the fish run out and then stops', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 3);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 8);

      expect(inventory('ana').countOf(ItemId.cookedFish), 3);
      expect(rolls.drawn, 3);
      expect(ana.notices, ['You reached level 2 in cooking.']);
    });

    test('burns the fish for a roll under the failure chance', () async {
      await start(_fails);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);

      expect(inventory('ana').countOf(ItemId.rawFish), 0);
      expect(inventory('ana').countOf(ItemId.cookedFish), 0);
      expect(inventory('ana').countOf(ItemId.burntFish), 1);
      expect(server.skills.xpOf('ana', 'cooking'), 0);
      expect(ana.notices, ['You fail to cook the fish.']);
    });

    test('keeps going after a burnt fish', () async {
      await start([0.0, 0.99, 0.99]);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 3);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 8);

      expect(inventory('ana').countOf(ItemId.burntFish), 1);
      expect(inventory('ana').countOf(ItemId.cookedFish), 2);
      expect(inventory('ana').countOf(ItemId.rawFish), 0);
    });

    test('never burns at the safe level', () async {
      await start(_fails);
      final ana = await join('Ana');
      level('ana', 'cooking', 30);
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);

      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
      expect(inventory('ana').countOf(ItemId.burntFish), 0);
    });

    test('stops when the fire goes out', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 3);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);
      server.objects.remove(fire);
      await tick(_makeTicks * 4);

      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
      expect(inventory('ana').countOf(ItemId.rawFish), 2);
      expect(ana.notices.last, 'The fire has gone.');
    });

    test('stops when the player walks away', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 3);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);
      await ana.send(Walk(at(40, 40)));
      await tick(_makeTicks * 4);

      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
    });

    test('stops when the player leaves', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 3);
      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);
      final before = rolls.drawn;

      await server.host.removeClient('ana');
      await tick(_makeTicks * 3);

      expect(rolls.drawn, before);
    });
  });

  group('a refusal', () {
    test('one level short says which level', () async {
      await start(_works, recipes: _only(_cook(level: 5)));
      final ana = await join('Ana');
      level('ana', 'cooking', 4);
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 2);

      expect(ana.notices, ['You need level 5 in cooking to do that.']);
      expect(inventory('ana').countOf(ItemId.rawFish), 1);
      expect(rolls.drawn, 0);
    });

    test('at the level needed makes', () async {
      await start(_works, recipes: _only(_cook(level: 5)));
      final ana = await join('Ana');
      level('ana', 'cooking', 5);
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);

      expect(ana.notices, isEmpty);
      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
    });

    test('without the tool says which tool', () async {
      await start(_works);
      final ana = await join('Ana');
      level('ana', 'smithing', 4);
      inventory('ana').add(ItemId.bronzeBar, 1);
      final anvil = server.objects.place(ObjectId.anvil, at(33, 33));

      await ana.useOnFire(0, anvil.networkId);
      await tick(_makeTicks * 2);

      expect(ana.notices, ['You need a hammer to do that.']);
      expect(inventory('ana').countOf(ItemId.bronzeBar), 1);
    });

    test('a tool that is worn counts as held', () async {
      final recipe = Recipe(
        name: 'cook the fish',
        skill: 'cooking',
        level: 1,
        xp: 30,
        start: const OnObject(ItemId.rawFish, ObjectId.fire),
        inputs: [ItemStack(ItemId.rawFish, 1)],
        product: Gives(ItemStack(ItemId.cookedFish, 1)),
        tool: ItemId.bronzeAxe,
      );
      await start(_works, recipes: _only(recipe));
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 1);
      server.items
          .container('ana', ContainerId.equipment)!
          .add(ItemId.bronzeAxe, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks + 1);

      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
    });

    test('with too few inputs changes nothing', () async {
      await start(_works, recipes: _only(_cook(inputs: 2)));
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 2);

      expect(ana.notices, ['You do not have what you need.']);
      expect(inventory('ana').countOf(ItemId.rawFish), 1);
      expect(rolls.drawn, 0);
    });

    test('with no room for the product changes nothing', () async {
      final recipe = Recipe(
        name: 'cook the feathers',
        skill: 'cooking',
        level: 1,
        xp: 30,
        start: const OnObject(ItemId.feathers, ObjectId.fire),
        inputs: [ItemStack(ItemId.feathers, 1)],
        product: Gives(ItemStack(ItemId.bronzeHelmet, 1)),
      );
      await start(_works, recipes: _only(recipe));
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.feathers, 5)
        ..add(ItemId.logs, inventorySize - 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 4);

      expect(ana.notices, ['You have no room for that.']);
      expect(inventory('ana').countOf(ItemId.feathers), 5);
      expect(inventory('ana').countOf(ItemId.bronzeHelmet), 0);
      expect(server.skills.xpOf('ana', 'cooking'), 0);
    });

    test('with no room for what a failure gives changes nothing', () async {
      final recipe = Recipe(
        name: 'cook the feathers',
        skill: 'cooking',
        level: 1,
        xp: 30,
        start: const OnObject(ItemId.feathers, ObjectId.fire),
        inputs: [ItemStack(ItemId.feathers, 1)],
        product: Gives(ItemStack(ItemId.bronzeSword, 1)),
        failure: Failure(chance: 1, safeLevel: 5, item: ItemId.bronzeHelmet),
      );
      await start(_fails, recipes: _only(recipe));
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.feathers, 5)
        ..add(ItemId.logs, inventorySize - 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 4);

      expect(ana.notices, ['You have no room for that.']);
      expect(inventory('ana').countOf(ItemId.feathers), 5);
    });

    test('with only some of the inputs changes nothing', () async {
      final recipe = Recipe(
        name: 'cook the feathers',
        skill: 'cooking',
        level: 1,
        xp: 30,
        start: const OnObject(ItemId.rawFish, ObjectId.fire),
        inputs: [ItemStack(ItemId.rawFish, 1), ItemStack(ItemId.feathers, 1)],
        product: Gives(ItemStack(ItemId.cookedFish, 1)),
      );
      await start(_works, recipes: _only(recipe));
      final ana = await join('Ana');
      inventory('ana').add(ItemId.rawFish, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 2);

      expect(ana.notices, ['You do not have what you need.']);
      expect(inventory('ana').countOf(ItemId.rawFish), 1);
    });

    test('is told on the tick of the click and queues nothing', () async {
      await start(_works);
      server.objects.place(ObjectId.fire, spawnTile);
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.tinderbox, 1)
        ..add(ItemId.logs, 1);

      await ana.useOnItem(0, 1);
      await tick(2);

      expect(ana.notices, ['You cannot light a fire here.']);
    });

    test('an item with no recipe on a fire does nothing', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana').add(ItemId.logs, 1);

      await ana.useOnFire(0, fire.networkId);
      await tick(_makeTicks * 2);

      expect(ana.notices, ['Nothing interesting happens.']);
      expect(rolls.drawn, 0);
    });
  });

  group('lighting a fire', () {
    Future<TestClient> lighter() async {
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.tinderbox, 1)
        ..add(ItemId.logs, 2);
      return ana;
    }

    test('uses the logs and stands a fire where the player is', () async {
      await start(_works);
      final ana = await lighter();

      await ana.useOnItem(0, 1);
      await tick(_makeTicks + 1);

      expect(inventory('ana').countOf(ItemId.logs), 1);
      expect(inventory('ana').countOf(ItemId.tinderbox), 1);
      expect(server.objects.at(spawnTile)?.def.id, ObjectId.fire);
      expect(server.skills.xpOf('ana', 'firemaking'), 40);
    });

    test('goes out after its ticks', () async {
      await start(_works);
      final ana = await lighter();
      await ana.useOnItem(0, 1);
      await tick(_makeTicks + 1);
      final lit = server.objects.at(spawnTile)!;
      final ticks =
          (defaultRecipes.recipes
                      .firstWhere((r) => r.skill == 'firemaking')
                      .product
                  as Lights)
              .ticks;

      await tick(ticks - 1);
      expect(server.objects.find(lit.networkId), isNotNull);

      await tick();
      expect(server.objects.find(lit.networkId), isNull);
      expect(server.objects.at(spawnTile), isNull);
    });

    test('is refused where a fire already stands', () async {
      await start(_works);
      final ana = await lighter();
      await ana.useOnItem(0, 1);
      await tick(_makeTicks + 1);

      await ana.useOnItem(0, 2);
      await tick(_makeTicks + 1);

      expect(ana.notices.last, 'You cannot light a fire here.');
      expect(inventory('ana').countOf(ItemId.logs), 1);
    });

    test('makes a station that cooking can use', () async {
      await start(_works);
      final ana = await lighter();
      inventory('ana').add(ItemId.rawFish, 1);
      await ana.useOnItem(0, 1);
      await tick(_makeTicks + 1);
      final lit = server.objects.at(spawnTile)!;

      await ana.useOnFire(3, lit.networkId);
      await tick(_makeTicks + 4);

      expect(inventory('ana').countOf(ItemId.cookedFish), 1);
    });
  });

  group('the other recipes', () {
    test('a knife carves logs into arrow shafts and is kept', () async {
      await start(_works);
      final ana = await join('Ana');
      inventory('ana')
        ..add(ItemId.knife, 1)
        ..add(ItemId.logs, 1);

      await ana.useOnItem(1, 0);
      await tick(_makeTicks + 1);

      expect(inventory('ana').countOf(ItemId.arrowShafts), 15);
      expect(inventory('ana').countOf(ItemId.logs), 0);
      expect(inventory('ana').countOf(ItemId.knife), 1);
      expect(server.skills.xpOf('ana', 'fletching'), 5);
    });

    test('a hammer forges a bar into a sword at an anvil', () async {
      await start(_works);
      final ana = await join('Ana');
      level('ana', 'smithing', 4);
      inventory('ana')
        ..add(ItemId.hammer, 1)
        ..add(ItemId.bronzeBar, 1);
      final anvil = server.objects.place(ObjectId.anvil, at(33, 33));

      await ana.useOnFire(1, anvil.networkId);
      await tick(_makeTicks + 2);
      expect(inventory('ana').countOf(ItemId.bronzeSword), 0);

      await tick();
      expect(inventory('ana').countOf(ItemId.bronzeSword), 1);
      expect(inventory('ana').countOf(ItemId.hammer), 1);
    });
  });
}
