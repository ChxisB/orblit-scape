import 'dart:collection';

import 'package:scape_core/scape_core.dart';

import 'world.dart';

/// Every way the data files disagree with one another, one line each. Empty
/// when they agree. Each file is checked alone when it is built, so this is
/// what is left: a name in one file that no other file answers.
///
/// Run it before the server stocks the plan's map. The server flags the tiles
/// of a solid object blocked, and this check would then report every one.
List<String> checkContent({
  required ItemSet items,
  required ObjectSet objects,
  required NodeSet nodes,
  required NpcSet npcs,
  required RecipeSet recipes,
  required Story story,
  required WorldPlan world,
}) => [
  ..._story(npcs, story),
  ..._itemsExist(items, npcs, story),
  ..._itemsObtainable(
    items: items,
    nodes: nodes,
    npcs: npcs,
    recipes: recipes,
    story: story,
  ),
  ..._objectsStand(objects, recipes, world),
  ..._npcsStand(npcs, world),
  ..._ground(objects, npcs, world),
  ..._reach(objects, world),
];

String _quote(String text) => "'$text'";

Iterable<String> _story(NpcSet npcs, Story story) sync* {
  final keys = {for (final dialogue in story.dialogues) dialogue.key};
  final opened = {
    for (final npc in npcs.npcs)
      if (npc.dialogue != null) npc.dialogue!,
  };
  for (final npc in npcs.npcs) {
    final key = npc.dialogue;
    if (key != null && !keys.contains(key)) {
      yield '${npc.name} opens dialogue ${_quote(key)}, which does not exist.';
    }
  }
  for (final dialogue in story.dialogues) {
    if (!opened.contains(dialogue.key)) {
      yield 'Dialogue ${_quote(dialogue.key)} is opened by no creature.';
    }
    for (final name in dialogue.beforeNames) {
      if (!story.steps.containsKey(name)) {
        yield 'Dialogue ${_quote(dialogue.key)} runs step ${_quote(name)}, '
            'which the story does not define.';
      }
    }
  }
  yield* _stagesReached(story);
}

/// A stage that no step a dialogue runs moves a player to is a quest that
/// cannot be finished.
Iterable<String> _stagesReached(Story story) sync* {
  final run = {for (final dialogue in story.dialogues) ...dialogue.beforeNames};
  final reached = {
    for (final name in run)
      if (story.steps[name] case final step?) step,
  };
  for (final quest in story.book.quests) {
    for (var stage = 1; stage <= quest.last; stage++) {
      if (!reached.contains((quest: quest.key, stage: stage))) {
        yield 'Quest ${_quote(quest.key)} stage $stage is reached by no step '
            'that a dialogue runs.';
      }
    }
  }
}

/// The items a creature drops, a shop sells or a quest gives.
Iterable<(String, int)> _named(NpcSet npcs, Story story) sync* {
  for (final npc in npcs.npcs) {
    for (final item in npc.loot?.items ?? const <int>[]) {
      yield ('${npc.name} drops', item);
    }
    for (final line in npc.shop?.lines ?? const <ShopLine>[]) {
      yield ('${npc.name} sells', line.item);
    }
  }
  for (final MapEntry(:key, :value) in story.rewards.entries) {
    for (final stack in value.items) {
      yield ('Quest ${_quote(key)} gives', stack.item);
    }
  }
}

Iterable<String> _itemsExist(ItemSet items, NpcSet npcs, Story story) sync* {
  for (final (who, item) in _named(npcs, story)) {
    if (items.find(item) == null) {
      yield '$who item $item, which does not exist.';
    }
  }
}

Iterable<int> _needs(Recipe recipe) => [
  ?recipe.tool,
  for (final stack in recipe.inputs) stack.item,
];

/// What a player can end up holding. It starts with what shops sell,
/// creatures drop and quests give, then adds what a node or a recipe makes
/// once its tool and inputs are held. A recipe that needs its own product
/// adds nothing.
Set<int> _obtainable(
  NodeSet nodes,
  NpcSet npcs,
  RecipeSet recipes,
  Story story,
) {
  final got = {for (final (_, item) in _named(npcs, story)) item};
  var grew = true;
  while (grew) {
    final before = got.length;
    got.addAll([
      for (final node in nodes.nodes)
        if (got.contains(node.tool)) node.product,
      for (final recipe in recipes.recipes)
        if (_needs(recipe).every(got.contains)) ..._made(recipe),
    ]);
    grew = got.length > before;
  }
  return got;
}

/// An item that a node or a recipe asks the player to hold must be
/// obtainable.
Iterable<String> _itemsObtainable({
  required ItemSet items,
  required NodeSet nodes,
  required NpcSet npcs,
  required RecipeSet recipes,
  required Story story,
}) sync* {
  final got = _obtainable(nodes, npcs, recipes, story);
  final needed = {
    for (final node in nodes.nodes) node.tool,
    for (final recipe in recipes.recipes) ..._needs(recipe),
  };
  for (final item in needed.difference(got)) {
    final name = items.find(item)?.name ?? '$item';
    yield 'Nothing gives $name. No shop sells it, no creature drops it, no '
        'quest gives it and no node or recipe that can be used makes it.';
  }
}

Iterable<int> _made(Recipe recipe) => [
  if (recipe.product case Gives(:final stack)) stack.item,
  ?recipe.failure?.item,
];

/// A definition that is never put anywhere is data nobody can reach, unless
/// a recipe lights it.
Iterable<String> _objectsStand(
  ObjectSet objects,
  RecipeSet recipes,
  WorldPlan world,
) sync* {
  final stands = {
    for (final placement in world.objects) placement.object,
    for (final recipe in recipes.recipes)
      if (recipe.product case Lights(:final object)) object,
  };
  for (final object in objects.objects) {
    if (!stands.contains(object.id)) {
      yield '${object.name} is never placed and no recipe lights it.';
    }
  }
}

Iterable<String> _npcsStand(NpcSet npcs, WorldPlan world) sync* {
  final stands = {for (final spawn in world.npcs) spawn.npc};
  for (final npc in npcs.npcs) {
    if (!stands.contains(npc.id)) yield '${npc.name} is never spawned.';
  }
}

bool _barred(TileMap map, TileCoord tile, TileFlags flags) =>
    map.flagsAt(tile).has(flags);

String _where(TileCoord tile) => '(${tile.x}, ${tile.y}, ${tile.plane})';

/// The tiles an object covers, or null when it runs off the world.
List<TileCoord>? _tiles(ObjectDef def, TileCoord at) {
  try {
    return def.footprintAt(at).tiles.toList();
  } on ArgumentError {
    return null;
  }
}

/// What the server refuses, found before it starts: a tile that is blocked,
/// water or taken, and stairs that lead nowhere.
Iterable<String> _ground(
  ObjectSet objects,
  NpcSet npcs,
  WorldPlan world,
) sync* {
  final taken = <TileCoord, ObjectDef>{};
  for (final placement in world.objects) {
    final def = objects.find(placement.object);
    if (def == null) {
      yield 'An object ${placement.object} at ${_where(placement.tile)} '
          'does not exist.';
    } else {
      yield* _footprint(world.map, def, placement, taken);
      yield* _stairs(def, placement);
    }
  }
  yield* _lands(objects, world, taken);
  yield* _creaturesOn(world, npcs, taken);
}

/// Where a new player appears and where stairs set a player down.
Iterable<String> _lands(
  ObjectSet objects,
  WorldPlan world,
  Map<TileCoord, ObjectDef> taken,
) sync* {
  final unfit = TileFlags.blocked | TileFlags.water;
  if (_barred(world.map, world.spawn, unfit)) {
    yield 'The spawn tile ${_where(world.spawn)} is blocked.';
  } else if (taken[world.spawn]?.solid ?? false) {
    yield 'The spawn tile ${_where(world.spawn)} is inside a solid object.';
  }
  for (final placement in world.objects) {
    final to = placement.destination;
    if (to == null) continue;
    final name = objects.find(placement.object)?.name ?? 'Stairs';
    final from = '$name at ${_where(placement.tile)} leads';
    if (_barred(world.map, to, unfit)) {
      yield '$from to blocked ground at ${_where(to)}.';
    } else if (taken[to]?.solid ?? false) {
      yield '$from into a solid object at ${_where(to)}.';
    }
  }
}

Iterable<String> _creaturesOn(
  WorldPlan world,
  NpcSet npcs,
  Map<TileCoord, ObjectDef> taken,
) sync* {
  final barred = TileFlags.blocked | TileFlags.water | TileFlags.noNpc;
  for (final spawn in world.npcs) {
    final def = npcs.find(spawn.npc);
    final name = def?.name ?? 'Creature ${spawn.npc}';
    if (def == null) {
      yield '$name at ${_where(spawn.tile)} does not exist.';
    } else if (_barred(world.map, spawn.tile, barred)) {
      yield '$name at ${_where(spawn.tile)} stands on ground it cannot use.';
    } else if (taken[spawn.tile]?.solid ?? false) {
      yield '$name at ${_where(spawn.tile)} stands inside a solid object.';
    }
  }
}

Iterable<String> _footprint(
  TileMap map,
  ObjectDef def,
  ObjectPlacement placement,
  Map<TileCoord, ObjectDef> taken,
) sync* {
  final tiles = _tiles(def, placement.tile);
  if (tiles == null) {
    yield '${def.name} at ${_where(placement.tile)} runs off the world.';
    return;
  }
  for (final tile in tiles) {
    if (_barred(map, tile, TileFlags.blocked | TileFlags.water)) {
      yield '${def.name} at ${_where(tile)} stands on blocked ground.';
    }
    if (taken.containsKey(tile)) {
      yield '${def.name} at ${_where(tile)} overlaps ${taken[tile]!.name}.';
    }
    taken[tile] = def;
  }
}

Iterable<String> _stairs(ObjectDef def, ObjectPlacement placement) sync* {
  final climbs = def.options.any((option) => option.action == 'climb');
  if (climbs && placement.destination == null) {
    yield '${def.name} at ${_where(placement.tile)} leads nowhere.';
  }
}

/// Everything must be reachable on foot from the spawn tile. A door counts as
/// open, because a player can open it, and a stair joins its two floors. A
/// player stands beside a thing and not on it, across no wall.
Iterable<String> _reach(ObjectSet objects, WorldPlan world) sync* {
  final reached = _walkable(objects, world);
  bool near(Iterable<TileCoord> tiles) => tiles.any(
    (tile) =>
        reached.contains(tile) ||
        _beside(tile).any(
          (from) => reached.contains(from) && world.map.canStep(from, tile),
        ),
  );
  for (final placement in world.objects) {
    final def = objects.find(placement.object);
    final tiles = def == null ? null : _tiles(def, placement.tile);
    if (tiles != null && !near(tiles)) {
      yield '${def!.name} at ${_where(placement.tile)} cannot be reached from '
          'the spawn tile.';
    }
  }
  for (final spawn in world.npcs) {
    if (!near([spawn.tile])) {
      yield 'A creature at ${_where(spawn.tile)} cannot be reached from the '
          'spawn tile.';
    }
  }
}

const _straight = [(0, -1), (-1, 0), (1, 0), (0, 1)];
const _diagonal = [(-1, -1), (1, -1), (-1, 1), (1, 1)];

/// The tiles [tile] touches at the given offsets that are in the world.
Iterable<TileCoord> _step(TileCoord tile, List<(int, int)> offsets) sync* {
  for (final (dx, dy) in offsets) {
    final x = tile.x + dx;
    final y = tile.y + dy;
    if (x >= 0 && y >= 0 && x < worldSize && y < worldSize) {
      yield TileCoord(x, y, tile.plane);
    }
  }
}

Iterable<TileCoord> _beside(TileCoord tile) => _step(tile, _straight);

Iterable<TileCoord> _around(TileCoord tile) =>
    _step(tile, [..._straight, ..._diagonal]);

/// The tiles a player can walk to from the spawn tile. A solid object that
/// cannot be opened stands in the way.
Set<TileCoord> _walkable(ObjectSet objects, WorldPlan world) {
  final links = {
    for (final placement in world.objects)
      if (placement.destination != null) placement.tile: placement.destination!,
  };
  final shut = _shut(objects, world);
  final seen = {world.spawn};
  final queue = Queue<TileCoord>.of([world.spawn]);
  while (queue.isNotEmpty) {
    final tile = queue.removeFirst();
    final climb = links[tile];
    if (climb != null && seen.add(climb)) queue.add(climb);
    for (final to in _around(tile)) {
      if (!shut.contains(to) && world.map.canStep(tile, to) && seen.add(to)) {
        queue.add(to);
      }
    }
  }
  return seen;
}

/// The tiles of solid objects with nothing to open or close.
Set<TileCoord> _shut(ObjectSet objects, WorldPlan world) => {
  for (final placement in world.objects)
    if (objects.find(placement.object) case final def?)
      if (def.solid && !def.options.any((o) => o.action == 'toggle'))
        ...?_tiles(def, placement.tile),
};
