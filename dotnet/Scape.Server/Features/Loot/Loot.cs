using Scape.Core;

namespace Scape.Server;

/// <summary>What loot asks of the rest of the server.</summary>
public sealed record LootOptions
{
    /// <summary>Says when a creature dies, and who killed it.</summary>
    public required Combat Combat { get; init; }

    public required GroundItems Ground { get; init; }

    public required ItemSet Items { get; init; }

    public required NpcSet Npcs { get; init; }

    public required Random Random { get; init; }
}

/// <summary>Puts what a creature's loot table pays on the ground where it died. The killer sees it first.</summary>
public static class Loot
{
    /// <summary>Adds loot to the server.</summary>
    /// <exception cref="ArgumentException">
    /// A table names an item that does not exist, so a typo fails at startup and not at the first kill.
    /// </exception>
    public static void Register(LootOptions options)
    {
        foreach (var item in options.Npcs.Npcs.SelectMany(npc => npc.Loot?.Items ?? []))
        {
            options.Items.Require(item);
        }

        options.Combat.AddKillHandler((killer, npc) =>
        {
            foreach (var stack in npc.Def.Loot?.Roll(options.Random) ?? [])
            {
                options.Ground.Drop(stack.Item, stack.Count, npc.Tile, killer);
            }
        });
    }
}
