import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

/// The problems of the default data, with one part swapped for a broken one.
List<String> problems({
  ObjectSet? objects,
  NpcSet? npcs,
  RecipeSet? recipes,
  Story? story,
  WorldPlan? world,
}) => checkContent(
  items: defaultItems,
  objects: objects ?? defaultObjects,
  nodes: defaultNodes,
  npcs: npcs ?? defaultNpcs,
  recipes: recipes ?? defaultRecipes,
  story: story ?? defaultStory,
  world: world ?? starterIsland(),
);

/// A fresh island with some parts replaced. The map is the island's own.
WorldPlan island({
  TileCoord? spawn,
  List<ObjectPlacement> Function(List<ObjectPlacement>)? objects,
  List<NpcSpawn> Function(List<NpcSpawn>)? npcs,
}) {
  final base = starterIsland();
  return WorldPlan(
    map: base.map,
    spawn: spawn ?? base.spawn,
    objects: objects?.call(base.objects) ?? base.objects,
    npcs: npcs?.call(base.npcs) ?? base.npcs,
  );
}

WorldPlan without(bool Function(ObjectPlacement) drop) =>
    island(objects: (all) => all.where((p) => !drop(p)).toList());

WorldPlan withNpcs(List<NpcSpawn> extra) =>
    island(npcs: (all) => [...all, ...extra]);

WorldPlan withObjects(List<ObjectPlacement> extra) =>
    island(objects: (all) => [...all, ...extra]);

/// The default story with its dialogues swapped for [dialogues].
Story storyWith(List<Dialogue> dialogues) => Story(
  quests: defaultStory.book.quests.toList(),
  dialogues: dialogues,
  steps: defaultStory.steps,
  rewards: defaultStory.rewards,
);

List<Dialogue> dialoguesWithout(String key) => [
  for (final d in defaultStory.dialogues)
    if (d.key != key) d,
];

NpcSet npcsWith(NpcDef extra) => NpcSet(npcs: [...defaultNpcs.npcs, extra]);

NpcSet shopWithout(int item) {
  final shop = defaultNpcs.require(NpcId.shopkeeper);
  return NpcSet(
    npcs: [
      for (final npc in defaultNpcs.npcs)
        if (npc.id == NpcId.shopkeeper)
          NpcDef(
            id: npc.id,
            name: npc.name,
            dialogue: npc.dialogue,
            shop: ShopDef(
              lines: [
                for (final line in shop.shop!.lines)
                  if (line.item != item) line,
              ],
            ),
          )
        else
          npc,
    ],
  );
}

void main() {
  test('the default data agrees with itself', () {
    expect(problems(), isEmpty);
  });

  test('the island has five regions and an open spawn tile', () {
    final island = starterIsland();

    expect(island.map.regionCount, 5);
    expect(island.map.flagsAt(island.spawn), TileFlags.open);
  });

  test('two calls give two maps, since the server writes to its own', () {
    final a = starterIsland();
    final b = starterIsland();
    a.map.setFlags(a.spawn, TileFlags.blocked);

    expect(b.map.flagsAt(b.spawn), TileFlags.open);
  });

  test('the Guard counts the four goblins that are there', () {
    final goblins = starterIsland().npcs.where((s) => s.npc == NpcId.goblin);

    expect(goblins, hasLength(4));
  });

  test('a fishing spot stands beside water', () {
    final island = starterIsland();
    for (final p in island.objects) {
      if (p.object != ObjectId.fishingSpot) continue;
      final beside = [
        for (final (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)])
          island.map.flagsAt(TileCoord(p.tile.x + dx, p.tile.y + dy, 0)),
      ];

      expect(beside.any((f) => f.has(TileFlags.water)), isTrue, reason: '$p');
    }
  });

  group('names that nothing answers', () {
    test('a creature opens a dialogue the story lacks', () {
      final found = problems(
        npcs: npcsWith(NpcDef(id: 6, name: 'Ghost', dialogue: 'ghost')),
        world: withNpcs([NpcSpawn(npc: 6, tile: TileCoord(62, 58, 0))]),
      );

      expect(
        found,
        contains("Ghost opens dialogue 'ghost', which does not exist."),
      );
    });

    test('a dialogue runs a step the story does not define', () {
      final stray = Dialogue.parse('''
{"kind": "orblit.dialogue", "formatVersion": 1, "key": "guard",
 "variants": [{"start": "a"}],
 "nodes": {"a": {"type": "line", "speaker": "G", "text": "Hi", "before": "nope"}}}
''');
      final found = problems(
        story: storyWith([...dialoguesWithout('guard'), stray]),
      );

      expect(
        found,
        contains(
          "Dialogue 'guard' runs step 'nope', which the story does not define.",
        ),
      );
    });

    test('a stage that no dialogue reaches cannot be finished', () {
      final found = problems(story: storyWith(dialoguesWithout('guard')));

      expect(
        found,
        contains(
          "Quest 'tally' stage 2 is reached by no step that a dialogue runs.",
        ),
      );
    });

    test('a dialogue that no creature opens', () {
      final extra = Dialogue.parse('''
{"kind": "orblit.dialogue", "formatVersion": 1, "key": "extra",
 "variants": [{"start": "a"}],
 "nodes": {"a": {"type": "line", "speaker": "X", "text": "Hi"}}}
''');
      final found = problems(
        story: storyWith([...defaultStory.dialogues, extra]),
      );

      expect(found, ["Dialogue 'extra' is opened by no creature."]);
    });

    test('a creature that is never spawned', () {
      final found = problems(npcs: npcsWith(NpcDef(id: 6, name: 'Ghost')));

      expect(found, ['Ghost is never spawned.']);
    });

    test('a loot table drops an item the game lacks', () {
      final found = problems(
        npcs: npcsWith(
          NpcDef(
            id: 6,
            name: 'Crab',
            loot: LootTable(always: [ItemStack(900, 1)]),
          ),
        ),
        world: withNpcs([NpcSpawn(npc: 6, tile: TileCoord(62, 58, 0))]),
      );

      expect(found, contains('Crab drops item 900, which does not exist.'));
    });
  });

  group('what cannot be got', () {
    test('a recipe whose station stands nowhere', () {
      final found = problems(world: without((p) => p.object == ObjectId.anvil));

      expect(found, contains('Anvil is never placed and no recipe lights it.'));
    });

    test('a recipe tool that no shop sells', () {
      expect(
        problems(npcs: shopWithout(ItemId.hammer)),
        contains(startsWith('Nothing gives Hammer.')),
      );
    });

    test('a node tool that no shop sells', () {
      final found = problems(npcs: shopWithout(ItemId.fishingNet));

      expect(found, contains(startsWith('Nothing gives Fishing net.')));
      expect(
        found,
        contains(startsWith('Nothing gives Raw fish.')),
        reason: 'the spots need the net, and the fire needs the fish',
      );
    });

    test('a recipe that needs its own product', () {
      final recipes = RecipeSet(
        items: defaultItems,
        objects: defaultObjects,
        skills: defaultSkills,
        recipes: [
          for (final r in defaultRecipes.recipes)
            if (r.name == 'smelt the bar')
              Recipe(
                name: r.name,
                skill: r.skill,
                level: r.level,
                xp: r.xp,
                start: const OnObject(ItemId.bronzeBar, ObjectId.furnace),
                inputs: [ItemStack(ItemId.bronzeBar, 1)],
                product: Gives(ItemStack(ItemId.bronzeBar, 1)),
              )
            else
              r,
        ],
      );

      expect(
        problems(recipes: recipes),
        contains(startsWith('Nothing gives Bronze bar.')),
      );
    });

    test('an input that no recipe makes', () {
      final recipes = RecipeSet(
        items: defaultItems,
        objects: defaultObjects,
        skills: defaultSkills,
        recipes: [
          for (final r in defaultRecipes.recipes)
            if (r.name != 'smelt the bar') r,
        ],
      );

      expect(
        problems(recipes: recipes),
        contains(startsWith('Nothing gives Bronze bar.')),
      );
    });
  });

  group('the ground', () {
    test('a creature in the sea and one inside a tree', () {
      final tree = starterIsland().objects.firstWhere(
        (p) => p.object == ObjectId.tree,
      );
      final found = problems(
        world: withNpcs([
          NpcSpawn(npc: NpcId.goblin, tile: TileCoord(1, 1, 0)),
          NpcSpawn(npc: NpcId.goblin, tile: tree.tile),
        ]),
      );

      expect(
        found,
        containsAll([
          'Goblin at (1, 1, 0) stands on ground it cannot use.',
          'Goblin at (${tree.tile.x}, ${tree.tile.y}, 0) stands inside a solid object.',
        ]),
      );
    });

    test('a creature on a no-creature tile, and one on stairs', () {
      final plan = island();
      plan.map.setFlags(TileCoord(62, 58, 0), TileFlags.noNpc);
      final stairs = plan.objects.firstWhere(
        (p) => p.object == ObjectId.stairsUp,
      );
      final found = problems(
        world: WorldPlan(
          map: plan.map,
          spawn: plan.spawn,
          objects: plan.objects,
          npcs: [
            ...plan.npcs,
            NpcSpawn(npc: NpcId.goblin, tile: TileCoord(62, 58, 0)),
            NpcSpawn(npc: NpcId.goblin, tile: stairs.tile),
          ],
        ),
      );

      expect(found, ['Goblin at (62, 58, 0) stands on ground it cannot use.']);
    });

    test('a creature that has no definition', () {
      final found = problems(
        world: withNpcs([NpcSpawn(npc: 99, tile: TileCoord(62, 58, 0))]),
      );

      expect(found, ['Creature 99 at (62, 58, 0) does not exist.']);
    });

    test('an object on the water and two on one tile', () {
      final found = problems(
        world: withObjects([
          ObjectPlacement(object: ObjectId.rock, tile: TileCoord(1, 1, 0)),
          ObjectPlacement(object: ObjectId.rock, tile: TileCoord(100, 60, 0)),
        ]),
      );

      expect(found, contains('Rock at (1, 1, 0) stands on blocked ground.'));
      expect(found, contains('Rock at (100, 60, 0) overlaps Rock.'));
    });

    test('an object that runs off the world is a line, not a crash', () {
      final wide = ObjectDef(id: 99, name: 'Hall', width: 2, height: 2);
      final edge = TileCoord(worldSize - 1, worldSize - 1, 0);
      final found = problems(
        objects: ObjectSet(objects: [...defaultObjects.objects, wide]),
        world: withObjects([ObjectPlacement(object: 99, tile: edge)]),
      );

      expect(found, contains('Hall at (16383, 16383, 0) runs off the world.'));
    });

    test('stairs with no way', () {
      final found = problems(
        world: island(
          objects: (all) => [
            for (final p in all)
              if (p.object != ObjectId.stairsUp) p,
            ObjectPlacement(
              object: ObjectId.stairsUp,
              tile: TileCoord(69, 85, 0),
            ),
          ],
        ),
      );

      expect(found, contains('Stairs at (69, 85, 0) leads nowhere.'));
    });

    test('stairs into a wall and stairs into a tree', () {
      final tree = starterIsland().objects.firstWhere(
        (p) => p.object == ObjectId.tree,
      );
      final plan = withObjects([
        ObjectPlacement(
          object: ObjectId.stairsDown,
          tile: TileCoord(60, 60, 0),
          destination: TileCoord(1, 1, 0),
        ),
        ObjectPlacement(
          object: ObjectId.stairsDown,
          tile: TileCoord(58, 58, 0),
          destination: tree.tile,
        ),
      ]);

      expect(problems(world: plan), [
        'Stairs at (60, 60, 0) leads to blocked ground at (1, 1, 0).',
        'Stairs at (58, 58, 0) leads into a solid object at '
            '(${tree.tile.x}, ${tree.tile.y}, 0).',
      ]);
    });

    test('a spawn tile in the sea and one inside a tree', () {
      final tree = starterIsland().objects.firstWhere(
        (p) => p.object == ObjectId.tree,
      );

      expect(
        problems(world: island(spawn: TileCoord(1, 1, 0))),
        contains('The spawn tile (1, 1, 0) is blocked.'),
      );
      expect(
        problems(world: island(spawn: tree.tile)),
        contains(
          'The spawn tile (${tree.tile.x}, ${tree.tile.y}, 0) is inside a '
          'solid object.',
        ),
      );
    });
  });

  group('reach', () {
    test('a room with the door walled up cannot be entered', () {
      final plan = island();
      plan.map.setFlags(
        TileCoord(56, 62, 0),
        TileFlags.wallSouth | TileFlags.wallEast | TileFlags.wallWest,
      );

      expect(
        problems(world: plan),
        contains(
          'A creature at (56, 65, 0) cannot be reached from the spawn tile.',
        ),
      );
    });

    test('a bank booth behind a wall is not reached from outside it', () {
      final plan = island();
      plan.map.setFlags(
        TileCoord(71, 62, 0),
        TileFlags.wallSouth | TileFlags.wallEast | TileFlags.wallWest,
      );
      final found = problems(world: plan);

      expect(
        found,
        containsAll([
          'Bank booth at (70, 66, 0) cannot be reached from the spawn tile.',
          'Bank booth at (72, 66, 0) cannot be reached from the spawn tile.',
        ]),
      );
    });

    test('rocks standing round a tile shut it off', () {
      final rocks = [
        for (final (x, y) in [
          (61, 56), (62, 56), (63, 56), (61, 57), (63, 57), (61, 58),
          (62, 58), (63, 58), //
        ])
          ObjectPlacement(object: ObjectId.rock, tile: TileCoord(x, y, 0)),
      ];
      final found = problems(
        world: island(
          objects: (all) => [...all, ...rocks],
          npcs: (all) => [
            for (final s in all)
              if (s.npc != NpcId.guard) s,
            NpcSpawn(npc: NpcId.guard, tile: TileCoord(62, 57, 0)),
          ],
        ),
      );

      expect(found, [
        'A creature at (62, 57, 0) cannot be reached from the spawn tile.',
      ]);
    });

    test('without the stairs the floor above is out of reach', () {
      final found = problems(
        world: without((p) => p.object == ObjectId.stairsUp),
      );

      expect(
        found,
        contains(
          'Stairs at (69, 85, 1) cannot be reached from the spawn tile.',
        ),
      );
    });
  });
}
