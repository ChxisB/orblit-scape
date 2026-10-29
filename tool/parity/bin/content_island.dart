import 'dart:math';

import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

/// One broken island: the default data with parts dropped, added or changed.
/// Indices name the island's own placements and spawns, in order.
typedef _Break = ({
  TileCoord? spawn,
  List<(TileCoord, int)> flags,
  List<int> dropObjects,
  List<ObjectPlacement> addObjects,
  List<int> dropNpcs,
  List<NpcSpawn> addNpcs,
  List<int> dropShopItems,
  List<String> dropRecipes,
  List<String> dropDialogues,
});

List<int> _tile(TileCoord tile) => [tile.x, tile.y, tile.plane];

List<Object?> _placement(ObjectPlacement p) => [
  p.object,
  _tile(p.tile),
  switch (p.destination) {
    final to? => _tile(to),
    null => null,
  },
];

List<Object> _spawn(NpcSpawn s) => [s.npc, _tile(s.tile), s.radius];

_Json _region(int x, int y, int plane) {
  final region = starterIsland().map.regionAt(RegionKey(x, y, plane));
  return {
    'op': 'region',
    'key': [x, y, plane],
    'expect': region == null ? null : hex(region.flags),
  };
}

_Json _plan() {
  final plan = starterIsland();
  return {
    'op': 'plan',
    'expect': {
      'spawn': _tile(plan.spawn),
      'objects': [for (final p in plan.objects) _placement(p)],
      'npcs': [for (final s in plan.npcs) _spawn(s)],
    },
  };
}

NpcSet _shopWithout(List<int> items) => NpcSet(
  npcs: [
    for (final npc in defaultNpcs.npcs)
      if (npc.id == NpcId.shopkeeper)
        NpcDef(
          id: npc.id,
          name: npc.name,
          dialogue: npc.dialogue,
          shop: ShopDef(
            lines: [
              for (final line in npc.shop!.lines)
                if (!items.contains(line.item)) line,
            ],
          ),
        )
      else
        npc,
  ],
);

WorldPlan _world(_Break b) {
  final base = starterIsland();
  for (final (tile, bits) in b.flags) {
    base.map.setFlags(tile, TileFlags(bits));
  }
  return WorldPlan(
    map: base.map,
    spawn: b.spawn ?? base.spawn,
    objects: [
      for (final (i, p) in base.objects.indexed)
        if (!b.dropObjects.contains(i)) p,
      ...b.addObjects,
    ],
    npcs: [
      for (final (i, s) in base.npcs.indexed)
        if (!b.dropNpcs.contains(i)) s,
      ...b.addNpcs,
    ],
  );
}

List<String> _found(_Break b) => checkContent(
  items: defaultItems,
  objects: defaultObjects,
  nodes: defaultNodes,
  npcs: _shopWithout(b.dropShopItems),
  recipes: RecipeSet(
    items: defaultItems,
    objects: defaultObjects,
    skills: defaultSkills,
    recipes: [
      for (final r in defaultRecipes.recipes)
        if (!b.dropRecipes.contains(r.name)) r,
    ],
  ),
  story: Story(
    quests: defaultStory.book.quests.toList(),
    dialogues: [
      for (final d in defaultStory.dialogues)
        if (!b.dropDialogues.contains(d.key)) d,
    ],
    steps: defaultStory.steps,
    rewards: defaultStory.rewards,
  ),
  world: _world(b),
);

_Json _problems(_Break b) => {
  'op': 'problems',
  'spawn': switch (b.spawn) {
    final tile? => _tile(tile),
    null => null,
  },
  'flags': [
    for (final (tile, bits) in b.flags) [..._tile(tile), bits],
  ],
  'dropObjects': b.dropObjects,
  'addObjects': [for (final p in b.addObjects) _placement(p)],
  'dropNpcs': b.dropNpcs,
  'addNpcs': [for (final s in b.addNpcs) _spawn(s)],
  'dropShopItems': b.dropShopItems,
  'dropRecipes': b.dropRecipes,
  'dropDialogues': b.dropDialogues,
  'expect': _found(b),
};

/// The default island with a few parts changed. Random breaks rarely land on
/// a solid object or on blocked dry ground, and drop one shop line at most, so
/// those cases are written out.
_Break _only({
  TileCoord? spawn,
  List<ObjectPlacement> addObjects = const [],
  List<int> dropShopItems = const [],
}) => (
  spawn: spawn,
  flags: const [],
  dropObjects: const [],
  addObjects: addObjects,
  dropNpcs: const [],
  addNpcs: const [],
  dropShopItems: dropShopItems,
  dropRecipes: const [],
  dropDialogues: const [],
);

/// Single flags, then a wall on three sides, which shuts a door.
const _flagBits = [0, 1, 2, 4, 8, 16, 32, 64, 8 | 4 | 16];

T _pick<T>(Random random, List<T> from) => from[random.nextInt(from.length)];

/// Up to [most] picks, each kept once, or none [chance] in 100 times out of
/// the rest.
List<T> _some<T>(Random random, int chance, int most, T Function() pick) =>
    random.nextInt(100) < chance
    ? {for (var i = random.nextInt(most) + 1; i > 0; i--) pick()}.toList()
    : [];

/// A tile within two of something on the island, or anywhere on plane 0.
TileCoord _tileFor(Random random) {
  final island = starterIsland();
  if (random.nextInt(4) == 0) {
    return TileCoord(random.nextInt(128), random.nextInt(128), 0);
  }
  final at = _pick(random, [
    island.spawn,
    for (final p in island.objects) p.tile,
    for (final s in island.npcs) s.tile,
  ]);
  return TileCoord(
    at.x + random.nextInt(5) - 2,
    at.y + random.nextInt(5) - 2,
    at.plane,
  );
}

ObjectPlacement _randomPlacement(Random random) {
  final id = _pick(random, [for (final o in defaultObjects.objects) o.id, 99]);
  final climbs = id == ObjectId.stairsUp || id == ObjectId.stairsDown;
  return ObjectPlacement(
    object: id,
    tile: _tileFor(random),
    destination: random.nextInt(10) < (climbs ? 7 : 1)
        ? _tileFor(random)
        : null,
  );
}

NpcSpawn _randomSpawn(Random random) => NpcSpawn(
  npc: _pick(random, [for (final n in defaultNpcs.npcs) n.id, 99]),
  tile: _tileFor(random),
  radius: random.nextInt(4),
);

_Break _randomBreak(Random random) {
  final island = starterIsland();
  final shop = defaultNpcs.require(NpcId.shopkeeper).shop!;
  return (
    spawn: random.nextInt(10) == 0 ? _tileFor(random) : null,
    flags: _some(
      random,
      50,
      4,
      () => (_tileFor(random), _pick(random, _flagBits)),
    ),
    dropObjects: _some(
      random,
      25,
      2,
      () => random.nextInt(island.objects.length),
    ),
    addObjects: _some(random, 40, 3, () => _randomPlacement(random)),
    dropNpcs: _some(random, 25, 2, () => random.nextInt(island.npcs.length)),
    addNpcs: _some(random, 40, 2, () => _randomSpawn(random)),
    dropShopItems: _some(random, 15, 1, () => _pick(random, shop.lines).item),
    dropRecipes: _some(
      random,
      15,
      1,
      () => _pick(random, defaultRecipes.recipes).name,
    ),
    dropDialogues: _some(
      random,
      10,
      1,
      () => _pick(random, defaultStory.dialogues).key,
    ),
  );
}

void main(List<String> args) {
  final random = Random(23);
  writeFixture(
    root: args.single,
    package: 'scape_content',
    name: 'island',
    cases: [
      for (var plane = 0; plane < 2; plane++)
        for (var y = 0; y < 3; y++)
          for (var x = 0; x < 3; x++) _region(x, y, plane),
      _plan(),
      _problems(_only()),
      _problems(_only(spawn: TileCoord(70, 66, 0))),
      _problems(
        _only(
          addObjects: [
            ObjectPlacement(
              object: ObjectId.stairsUp,
              tile: TileCoord(64, 60, 0),
              destination: TileCoord(50, 60, 0),
            ),
            ObjectPlacement(
              object: ObjectId.stairsUp,
              tile: TileCoord(64, 61, 0),
              destination: TileCoord(64, 64, 1),
            ),
          ],
        ),
      ),
      // Two recipes need logs, and the line for logs appears once.
      _problems(_only(dropShopItems: [ItemId.logs, ItemId.bronzeAxe])),
      for (var i = 0; i < 100; i++) _problems(_randomBreak(random)),
    ],
  );
}
