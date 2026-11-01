using Scape.Core;

namespace Scape.Content;

/// <summary>
/// What a player can make. A raw fish cooks on a fire and burns often at
/// first, never from level 30. Logs light with a tinderbox, or become arrow
/// shafts with a knife. Copper ore melts to a bar at a furnace, and a bar
/// becomes a sword at an anvil with a hammer.
/// </summary>
public static class DefaultRecipes
{
    public static RecipeSet All { get; } = new(
        [
            new Recipe(
                "cook the fish",
                "cooking",
                level: 1,
                xp: 30,
                new OnObject(ItemId.RawFish, ObjectId.Fire),
                [new ItemStack(ItemId.RawFish, 1)],
                new Gives(new ItemStack(ItemId.CookedFish, 1)),
                failure: new Failure(chance: 0.5, safeLevel: 30, item: ItemId.BurntFish)),
            new Recipe(
                "light the logs",
                "firemaking",
                level: 1,
                xp: 40,
                new OnItem(ItemId.Tinderbox, ItemId.Logs),
                [new ItemStack(ItemId.Logs, 1)],
                new Lights(ObjectId.Fire, ticks: 100),
                tool: ItemId.Tinderbox),
            new Recipe(
                "carve the logs",
                "fletching",
                level: 1,
                xp: 5,
                new OnItem(ItemId.Knife, ItemId.Logs),
                [new ItemStack(ItemId.Logs, 1)],
                new Gives(new ItemStack(ItemId.ArrowShafts, 15)),
                tool: ItemId.Knife),
            new Recipe(
                "smelt the bar",
                "smithing",
                level: 1,
                xp: 6,
                new OnObject(ItemId.CopperOre, ObjectId.Furnace),
                [new ItemStack(ItemId.CopperOre, 1)],
                new Gives(new ItemStack(ItemId.BronzeBar, 1))),
            new Recipe(
                "forge the sword",
                "smithing",
                level: 4,
                xp: 25,
                new OnObject(ItemId.BronzeBar, ObjectId.Anvil),
                [new ItemStack(ItemId.BronzeBar, 1)],
                new Gives(new ItemStack(ItemId.BronzeSword, 1)),
                tool: ItemId.Hammer,
                ticks: 4),
        ],
        DefaultItems.All,
        DefaultObjects.All,
        DefaultSkills.All);
}
