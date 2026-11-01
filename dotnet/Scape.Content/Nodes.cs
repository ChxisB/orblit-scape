using Scape.Core;

namespace Scape.Content;

/// <summary>
/// What each tree, rock and fishing spot gives. The numbers are the first
/// game's own. A rock runs dry often and comes back sooner, a fishing spot
/// seldom runs dry.
/// </summary>
public static class DefaultNodes
{
    public static NodeSet All { get; } = new(
        [
            new NodeDef(
                objectId: ObjectId.Tree,
                skill: "woodcutting",
                level: 1,
                tool: ItemId.BronzeAxe,
                xp: 25,
                product: ItemId.Logs,
                chanceLow: 0.3,
                chanceHigh: 0.7,
                fullLevel: 30,
                depleteChance: 0.15,
                respawnTicks: 25),
            new NodeDef(
                objectId: ObjectId.Rock,
                skill: "mining",
                level: 1,
                tool: ItemId.BronzePickaxe,
                xp: 18,
                product: ItemId.CopperOre,
                chanceLow: 0.25,
                chanceHigh: 0.65,
                fullLevel: 30,
                depleteChance: 0.4,
                respawnTicks: 15),
            new NodeDef(
                objectId: ObjectId.FishingSpot,
                skill: "fishing",
                level: 1,
                tool: ItemId.FishingNet,
                xp: 15,
                product: ItemId.RawFish,
                chanceLow: 0.35,
                chanceHigh: 0.75,
                fullLevel: 30,
                depleteChance: 0.05,
                respawnTicks: 20),
        ],
        DefaultObjects.All,
        DefaultItems.All,
        DefaultSkills.All);
}
