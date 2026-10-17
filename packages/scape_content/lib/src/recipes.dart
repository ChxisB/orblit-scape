import 'package:scape_core/scape_core.dart';

import 'items.dart';
import 'objects.dart';
import 'skills.dart';

/// What a player can make. A raw fish cooks on a fire and burns often at
/// first, never from level 30. Logs light with a tinderbox, or become arrow
/// shafts with a knife. A bar becomes a sword at an anvil with a hammer.
final RecipeSet defaultRecipes = RecipeSet(
  items: defaultItems,
  objects: defaultObjects,
  skills: defaultSkills,
  recipes: [
    Recipe(
      name: 'cook the fish',
      skill: 'cooking',
      level: 1,
      xp: 30,
      start: const OnObject(ItemId.rawFish, ObjectId.fire),
      inputs: [ItemStack(ItemId.rawFish, 1)],
      product: Gives(ItemStack(ItemId.cookedFish, 1)),
      failure: Failure(chance: 0.5, safeLevel: 30, item: ItemId.burntFish),
    ),
    Recipe(
      name: 'light the logs',
      skill: 'firemaking',
      level: 1,
      xp: 40,
      start: const OnItem(ItemId.tinderbox, ItemId.logs),
      inputs: [ItemStack(ItemId.logs, 1)],
      tool: ItemId.tinderbox,
      product: Lights(object: ObjectId.fire, ticks: 100),
    ),
    Recipe(
      name: 'carve the logs',
      skill: 'fletching',
      level: 1,
      xp: 5,
      start: const OnItem(ItemId.knife, ItemId.logs),
      inputs: [ItemStack(ItemId.logs, 1)],
      tool: ItemId.knife,
      product: Gives(ItemStack(ItemId.arrowShafts, 15)),
    ),
    Recipe(
      name: 'forge the sword',
      skill: 'smithing',
      level: 4,
      xp: 25,
      start: const OnObject(ItemId.bronzeBar, ObjectId.anvil),
      inputs: [ItemStack(ItemId.bronzeBar, 1)],
      tool: ItemId.hammer,
      product: Gives(ItemStack(ItemId.bronzeSword, 1)),
      ticks: 4,
    ),
  ],
);
