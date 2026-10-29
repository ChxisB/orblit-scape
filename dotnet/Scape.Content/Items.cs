using Scape.Core;

namespace Scape.Content;

/// <summary>
/// The ids of the first game's items. Content that names an item names it
/// here, so a typo is a compile error and not a missing item.
/// </summary>
public static class ItemId
{
    public const int Coins = 1;
    public const int Logs = 2;
    public const int CopperOre = 3;
    public const int BronzeBar = 4;
    public const int RawFish = 5;
    public const int CookedFish = 6;
    public const int BronzeAxe = 7;
    public const int BronzePickaxe = 8;
    public const int BronzeSword = 9;
    public const int WoodenShield = 10;
    public const int BronzeHelmet = 11;
    public const int FishingNet = 12;
    public const int Bones = 13;
    public const int Feathers = 14;
    public const int BurntFish = 15;
    public const int Knife = 16;
    public const int Tinderbox = 17;
    public const int ArrowShafts = 18;
    public const int Hammer = 19;
    public const int Shortbow = 20;
    public const int BronzeArrows = 21;
    public const int Runes = 22;
}

/// <summary>The items of the first game.</summary>
public static class DefaultItems
{
    public static ItemSet All { get; } = new(
        [
            new ItemDef(ItemId.Coins, "Coins", stackable: true, value: 1),
            new ItemDef(ItemId.Logs, "Logs", value: 4),
            new ItemDef(ItemId.CopperOre, "Copper ore", value: 6),
            new ItemDef(ItemId.BronzeBar, "Bronze bar", value: 20),
            new ItemDef(ItemId.RawFish, "Raw fish", value: 5),
            new ItemDef(ItemId.CookedFish, "Cooked fish", value: 9),
            new ItemDef(
                ItemId.BronzeAxe,
                "Bronze axe",
                value: 30,
                equipSlot: "weapon",
                needs: new Needs("woodcutting", 1),
                combat: new CombatBonus(attack: 3, strength: 4, speed: 5)),
            new ItemDef(
                ItemId.BronzePickaxe,
                "Bronze pickaxe",
                value: 30,
                equipSlot: "weapon",
                needs: new Needs("mining", 1),
                combat: new CombatBonus(attack: 2, strength: 3, speed: 5)),
            new ItemDef(
                ItemId.BronzeSword,
                "Bronze sword",
                value: 40,
                equipSlot: "weapon",
                needs: new Needs("fighting", 1),
                combat: new CombatBonus(attack: 8, strength: 6, speed: 4)),
            new ItemDef(
                ItemId.WoodenShield,
                "Wooden shield",
                value: 25,
                equipSlot: "shield",
                combat: new CombatBonus(defence: 6)),
            new ItemDef(
                ItemId.BronzeHelmet,
                "Bronze helmet",
                value: 35,
                equipSlot: "head",
                needs: new Needs("fighting", 1),
                combat: new CombatBonus(defence: 4)),
            new ItemDef(ItemId.FishingNet, "Fishing net", value: 8),
            new ItemDef(ItemId.Bones, "Bones", value: 1),
            new ItemDef(ItemId.Feathers, "Feathers", stackable: true, value: 2),
            new ItemDef(ItemId.BurntFish, "Burnt fish", value: 1),
            new ItemDef(ItemId.Knife, "Knife", value: 6),
            new ItemDef(ItemId.Tinderbox, "Tinderbox", value: 4),
            new ItemDef(ItemId.ArrowShafts, "Arrow shafts", stackable: true, value: 1),
            new ItemDef(ItemId.Hammer, "Hammer", value: 8),
            new ItemDef(
                ItemId.Shortbow,
                "Shortbow",
                value: 40,
                equipSlot: "weapon",
                combat: new CombatBonus(
                    attack: 6,
                    strength: 2,
                    speed: 5,
                    style: CombatStyle.Ranged,
                    range: 7,
                    ammo: "arrow")),
            new ItemDef(
                ItemId.BronzeArrows,
                "Bronze arrows",
                stackable: true,
                value: 1,
                equipSlot: "ammo",
                combat: new CombatBonus(strength: 4, ammo: "arrow")),
            new ItemDef(ItemId.Runes, "Runes", stackable: true, value: 2),
        ],
        DefaultSkills.All);
}
