import 'package:scape_core/scape_core.dart';

import 'items.dart';
import 'objects.dart';
import 'skills.dart';

/// What each tree, rock and fishing spot gives. The numbers are the first
/// game's own. A rock runs dry often and comes back sooner, a fishing spot
/// seldom runs dry.
final NodeSet defaultNodes = NodeSet(
  objects: defaultObjects,
  items: defaultItems,
  skills: defaultSkills,
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
      depleteChance: 0.15,
      respawnTicks: 25,
    ),
    NodeDef(
      object: ObjectId.rock,
      skill: 'mining',
      level: 1,
      tool: ItemId.bronzePickaxe,
      xp: 18,
      product: ItemId.copperOre,
      chanceLow: 0.25,
      chanceHigh: 0.65,
      fullLevel: 30,
      depleteChance: 0.4,
      respawnTicks: 15,
    ),
    NodeDef(
      object: ObjectId.fishingSpot,
      skill: 'fishing',
      level: 1,
      tool: ItemId.fishingNet,
      xp: 15,
      product: ItemId.rawFish,
      chanceLow: 0.35,
      chanceHigh: 0.75,
      fullLevel: 30,
      depleteChance: 0.05,
      respawnTicks: 20,
    ),
  ],
);
