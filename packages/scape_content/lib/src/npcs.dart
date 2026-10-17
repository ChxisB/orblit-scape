import 'package:scape_core/scape_core.dart';

import 'items.dart';

/// The ids of the creatures in [defaultNpcs].
abstract final class NpcId {
  static const int chicken = 1;
  static const int goblin = 2;
  static const int guard = 3;
  static const int shopkeeper = 4;
}

/// A chicken wanders, a goblin goes for anyone who comes near and a guard
/// stands where it is put. The chicken and the goblin drop things when they
/// die. The shopkeeper stands where it is put and sells tools.
final NpcSet defaultNpcs = NpcSet(
  npcs: [
    NpcDef(
      id: NpcId.chicken,
      name: 'Chicken',
      behaviour: const Wander(),
      fight: NpcFight(health: 3),
      loot: LootTable(
        always: [ItemStack(ItemId.bones, 1)],
        entries: [
          LootItem(weight: 7, item: ItemId.feathers, min: 1, max: 5),
          LootItem(weight: 2, item: ItemId.coins, min: 1, max: 3),
        ],
        nothing: 1,
      ),
    ),
    NpcDef(
      id: NpcId.goblin,
      name: 'Goblin',
      behaviour: Aggressive(sight: 5, leash: 12),
      fight: NpcFight(
        health: 8,
        attack: 3,
        strength: 3,
        defence: 2,
        respawnTicks: 40,
      ),
      loot: LootTable(
        always: [ItemStack(ItemId.bones, 1)],
        entries: [
          LootItem(weight: 6, item: ItemId.coins, min: 5, max: 20),
          LootItem(weight: 2, item: ItemId.copperOre, min: 1, max: 2),
          LootSub(
            weight: 2,
            table: LootTable(
              entries: [
                LootItem(weight: 2, item: ItemId.woodenShield),
                LootItem(weight: 1, item: ItemId.bronzeHelmet),
              ],
            ),
          ),
        ],
        nothing: 3,
        rare: LootTable(always: [ItemStack(ItemId.bronzeSword, 1)]),
        rareOneIn: 25,
      ),
    ),
    NpcDef(id: NpcId.guard, name: 'Guard'),
    NpcDef(
      id: NpcId.shopkeeper,
      name: 'Shopkeeper',
      shop: ShopDef(
        lines: [
          ShopLine(item: ItemId.bronzeAxe, cap: 5, restockTicks: 100),
          ShopLine(item: ItemId.bronzePickaxe, cap: 5, restockTicks: 100),
          ShopLine(item: ItemId.fishingNet, cap: 10, restockTicks: 50),
          ShopLine(item: ItemId.logs, cap: 40, start: 0, restockTicks: 200),
          ShopLine(
            item: ItemId.copperOre,
            cap: 40,
            start: 0,
            restockTicks: 200,
          ),
        ],
      ),
    ),
  ],
);
