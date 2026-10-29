using Scape.Core;

namespace Scape.Content;

/// <summary>The ids of the objects in <see cref="DefaultObjects"/>.</summary>
public static class ObjectId
{
    public const int Door = 1;
    public const int StairsUp = 2;
    public const int StairsDown = 3;
    public const int Tree = 4;
    public const int Rock = 5;
    public const int FishingSpot = 6;
    public const int BankBooth = 7;
    public const int Fire = 8;
    public const int Anvil = 9;
    public const int Furnace = 10;
}

/// <summary>
/// A door opens and shuts, and stairs lead to the next plane. Where stairs
/// lead is set where each one is placed. A tree, a rock and a fishing spot
/// can be gathered from while in state 0 and are bare in state 1. A bank
/// booth opens the player's bank. A fire, an anvil and a furnace offer
/// nothing to pick. Recipes use them as stations.
/// </summary>
public static class DefaultObjects
{
    public static ObjectSet All { get; } = new(
        [
            new ObjectDef(
                ObjectId.Door,
                "Door",
                solid: true,
                options: [new ObjectOption("Open", "toggle", whenState: 0), new ObjectOption("Close", "toggle", whenState: 1)]),
            new ObjectDef(ObjectId.StairsUp, "Stairs", options: [new ObjectOption("Climb up", "climb")]),
            new ObjectDef(ObjectId.StairsDown, "Stairs", options: [new ObjectOption("Climb down", "climb")]),
            new ObjectDef(ObjectId.Tree, "Tree", solid: true, options: [new ObjectOption("Chop down", "gather", whenState: 0)]),
            new ObjectDef(ObjectId.Rock, "Rock", solid: true, options: [new ObjectOption("Mine", "gather", whenState: 0)]),
            new ObjectDef(ObjectId.FishingSpot, "Fishing spot", options: [new ObjectOption("Net", "gather", whenState: 0)]),
            new ObjectDef(ObjectId.BankBooth, "Bank booth", solid: true, options: [new ObjectOption("Bank", "bank")]),
            new ObjectDef(ObjectId.Fire, "Fire"),
            new ObjectDef(ObjectId.Anvil, "Anvil", solid: true),
            new ObjectDef(ObjectId.Furnace, "Furnace", solid: true),
        ]);
}
