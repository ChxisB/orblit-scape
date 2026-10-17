import 'package:scape_core/scape_core.dart';

import 'skills.dart';

/// The ids of the first game's items. Content that names an item names it
/// here, so a typo is a compile error and not a missing item.
abstract final class ItemId {
  static const int coins = 1;
  static const int logs = 2;
  static const int copperOre = 3;
  static const int bronzeBar = 4;
  static const int rawFish = 5;
  static const int cookedFish = 6;
  static const int bronzeAxe = 7;
  static const int bronzePickaxe = 8;
  static const int bronzeSword = 9;
  static const int woodenShield = 10;
  static const int bronzeHelmet = 11;
}

/// The items of the first game.
final ItemSet defaultItems = ItemSet(
  skills: defaultSkills,
  items: [
    ItemDef(id: ItemId.coins, name: 'Coins', stackable: true, value: 1),
    ItemDef(id: ItemId.logs, name: 'Logs', value: 4),
    ItemDef(id: ItemId.copperOre, name: 'Copper ore', value: 6),
    ItemDef(id: ItemId.bronzeBar, name: 'Bronze bar', value: 20),
    ItemDef(id: ItemId.rawFish, name: 'Raw fish', value: 5),
    ItemDef(id: ItemId.cookedFish, name: 'Cooked fish', value: 9),
    ItemDef(
      id: ItemId.bronzeAxe,
      name: 'Bronze axe',
      value: 30,
      equipSlot: 'weapon',
      needs: (skill: 'woodcutting', level: 1),
    ),
    ItemDef(
      id: ItemId.bronzePickaxe,
      name: 'Bronze pickaxe',
      value: 30,
      equipSlot: 'weapon',
      needs: (skill: 'mining', level: 1),
    ),
    ItemDef(
      id: ItemId.bronzeSword,
      name: 'Bronze sword',
      value: 40,
      equipSlot: 'weapon',
      needs: (skill: 'fighting', level: 1),
    ),
    ItemDef(
      id: ItemId.woodenShield,
      name: 'Wooden shield',
      value: 25,
      equipSlot: 'shield',
    ),
    ItemDef(
      id: ItemId.bronzeHelmet,
      name: 'Bronze helmet',
      value: 35,
      equipSlot: 'head',
      needs: (skill: 'fighting', level: 1),
    ),
  ],
);
