import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/test_client.dart';

ScapeServer serverOn(WorldPlan world) => ScapeServer(
  clock: TickClock(period: tickPeriod, now: FakeTime().read),
  pacer: NoPacer(),
  log: (_) {},
  map: world.map,
  spawn: world.spawn,
  skills: defaultSkills,
  items: defaultItems,
  objects: defaultObjects,
  nodes: defaultNodes,
  npcs: defaultNpcs,
  story: defaultStory,
  shops: defaultShops,
  combat: defaultCombat,
  recipes: defaultRecipes,
);

void main() {
  test('the server takes in every object and creature of the island', () async {
    final world = starterIsland();
    final server = serverOn(world);

    server.stock(objects: world.objects, npcs: world.npcs);

    expect(server.npcs.count, world.npcs.length);
    for (final placement in world.objects) {
      expect(
        server.objects.at(placement.tile),
        isNotNull,
        reason: '$placement',
      );
    }
    await server.dispose();
  });

  test('a new player appears on the spawn tile of the island', () async {
    final world = starterIsland();
    final server = serverOn(world);
    server.stock(objects: world.objects, npcs: world.npcs);
    final ana = TestClient(server);

    await ana.login('Ana');
    server.loop.runTick();
    await pump();

    expect(server.movement.tileOf('ana'), world.spawn);
    await ana.dispose();
    await server.dispose();
  });

  test('a player opens the shop door and walks in to the shopkeeper', () async {
    final world = starterIsland();
    final server = serverOn(world);
    server.stock(objects: world.objects, npcs: world.npcs);
    final door = server.objects.at(TileCoord(56, 62, 0))!;
    final ana = TestClient(server);
    await ana.login('Ana');
    for (var i = 0; i < 2; i++) {
      server.loop.runTick();
      await pump();
    }

    await ana.send(
      Interact(kind: InteractKind.object, id: door.networkId, option: 0),
    );
    for (var i = 0; i < 40; i++) {
      server.loop.runTick();
      await pump();
    }
    await ana.send(Walk(TileCoord(56, 64, 0)));
    for (var i = 0; i < 12; i++) {
      server.loop.runTick();
      await pump();
    }

    expect(door.state, 1);
    expect(server.movement.tileOf('ana'), TileCoord(56, 64, 0));
    await ana.dispose();
    await server.dispose();
  });

  test('a player walks to the furnace and smelts a bar', () async {
    final world = starterIsland();
    final server = serverOn(world);
    server.stock(objects: world.objects, npcs: world.npcs);
    final furnace = server.objects.at(TileCoord(50, 60, 0))!;
    final ana = TestClient(server);
    await ana.login('Ana');
    server.loop.runTick();
    await pump();
    server.items
        .container('ana', ContainerId.inventory)!
        .add(ItemId.copperOre, 1);

    await ana.send(Walk(TileCoord(51, 60, 0)));
    for (var i = 0; i < 20; i++) {
      server.loop.runTick();
      await pump();
    }
    await ana.send(
      Interact(
        kind: InteractKind.useOnObject,
        id: furnace.networkId,
        option: 0,
      ),
    );
    for (var i = 0; i < 6; i++) {
      server.loop.runTick();
      await pump();
    }

    final bag = server.items.container('ana', ContainerId.inventory)!;
    expect(bag.countOf(ItemId.bronzeBar), 1);
    expect(bag.countOf(ItemId.copperOre), 0);
    await ana.dispose();
    await server.dispose();
  });

  test('a placement the world refuses is an error', () async {
    final world = starterIsland();
    final server = serverOn(world);

    expect(
      () => server.stock(
        objects: [
          ObjectPlacement(object: ObjectId.rock, tile: TileCoord(1, 1, 0)),
        ],
        npcs: const [],
      ),
      throwsArgumentError,
    );
    await server.dispose();
  });
}
