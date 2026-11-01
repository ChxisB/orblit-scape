using Scape.Core;

namespace Scape.Content;

/// <summary>The ids of the creatures in <see cref="DefaultNpcs"/>.</summary>
public static class NpcId
{
    public const int Chicken = 1;
    public const int Goblin = 2;
    public const int Guard = 3;
    public const int Shopkeeper = 4;
    public const int Lamplighter = 5;
}

/// <summary>
/// A chicken wanders, a goblin goes for anyone who comes near and a guard
/// stands where it is put. The chicken and the goblin drop things when they
/// die. The shopkeeper stands where it is put and sells tools. The guard, the
/// shopkeeper and the lamplighter talk.
/// </summary>
public static class DefaultNpcs
{
    public static NpcSet All { get; } = new(
        [
            new NpcDef(
                NpcId.Chicken,
                "Chicken",
                behaviour: new Wander(),
                fight: new NpcFight(health: 3),
                loot: new LootTable(
                    always: [new ItemStack(ItemId.Bones, 1)],
                    entries:
                    [
                        new LootItem(weight: 7, ItemId.Feathers, min: 1, max: 5),
                        new LootItem(weight: 2, ItemId.Coins, min: 1, max: 3),
                    ],
                    nothing: 1)),
            new NpcDef(
                NpcId.Goblin,
                "Goblin",
                behaviour: new Aggressive(sight: 5, leash: 12),
                fight: new NpcFight(health: 8, attack: 3, strength: 3, defence: 2, respawnTicks: 40),
                loot: new LootTable(
                    always: [new ItemStack(ItemId.Bones, 1)],
                    entries:
                    [
                        new LootItem(weight: 6, ItemId.Coins, min: 5, max: 20),
                        new LootItem(weight: 2, ItemId.CopperOre, min: 1, max: 2),
                        new LootSub(
                            weight: 2,
                            new LootTable(
                                entries: [new LootItem(weight: 2, ItemId.WoodenShield), new LootItem(weight: 1, ItemId.BronzeHelmet)])),
                    ],
                    nothing: 3,
                    rare: new LootTable(always: [new ItemStack(ItemId.BronzeSword, 1)]),
                    rareOneIn: 25)),
            new NpcDef(NpcId.Guard, "Guard", dialogue: "guard"),
            new NpcDef(NpcId.Lamplighter, "Brindle", dialogue: "brindle"),
            new NpcDef(
                NpcId.Shopkeeper,
                "Shopkeeper",
                dialogue: "shopkeeper",
                shop: new ShopDef(
                    [
                        new ShopLine(ItemId.BronzeAxe, cap: 5, restockTicks: 100),
                        new ShopLine(ItemId.BronzePickaxe, cap: 5, restockTicks: 100),
                        new ShopLine(ItemId.FishingNet, cap: 10, restockTicks: 50),
                        new ShopLine(ItemId.Logs, cap: 40, start: 0, restockTicks: 200),
                        new ShopLine(ItemId.CopperOre, cap: 40, start: 0, restockTicks: 200),
                        new ShopLine(ItemId.Tinderbox, cap: 5, restockTicks: 100),
                        new ShopLine(ItemId.Knife, cap: 5, restockTicks: 100),
                        new ShopLine(ItemId.Hammer, cap: 5, restockTicks: 100),
                        new ShopLine(ItemId.Shortbow, cap: 3, restockTicks: 200),
                        new ShopLine(ItemId.BronzeArrows, cap: 200, restockTicks: 10),
                        new ShopLine(ItemId.Runes, cap: 200, restockTicks: 10),
                    ])),
        ]);
}
