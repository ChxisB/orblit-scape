namespace Scape.Core;

/// <summary>The replicated component on a creature standing in the world.</summary>
public static class Npc
{
    public const string Name = "Npc";

    /// <summary>Ints in the component: the definition id.</summary>
    public const int Ints = 1;
}

/// <summary>The options of a creature, as the option byte of an interact message names them.</summary>
public static class NpcOption
{
    public const int Attack = 0;

    public const int Trade = 1;

    public const int Talk = 2;
}

/// <summary>What a creature does when nobody is making it do anything.</summary>
public abstract class Behaviour
{
    // Closed to this assembly, so the cases below are every behaviour.
    private protected Behaviour()
    {
    }
}

/// <summary>Stands where it was placed.</summary>
public sealed class Idle : Behaviour;

/// <summary>Walks about within the radius its spawn names.</summary>
public sealed class Wander : Behaviour;

/// <summary>
/// Wanders until a player comes within <see cref="Sight"/> tiles, then follows them. It
/// gives up when the player stands more than <see cref="Leash"/> tiles from where the
/// creature was placed, and walks home.
/// </summary>
public sealed class Aggressive : Behaviour
{
    public Aggressive(int sight, int leash)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(leash, sight);
        Sight = sight;
        Leash = leash;
    }

    public int Sight { get; }

    public int Leash { get; }
}

/// <summary>One kind of creature: a row of content.</summary>
public sealed class NpcDef
{
    /// <summary>The highest NPC id.</summary>
    public const int MaxId = 0xFFFF;

    // F2: a row of content names its fields, and most take their defaults.
    public NpcDef(
        int id,
        string name,
        Behaviour? behaviour = null,
        NpcFight? fight = null,
        LootTable? loot = null,
        ShopDef? shop = null,
        string? dialogue = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (dialogue is { Length: 0 })
        {
            throw new ArgumentException("The dialogue is empty.", nameof(dialogue));
        }

        Id = id;
        Name = name;
        Behaviour = behaviour ?? new Idle();
        Fight = fight;
        Loot = loot;
        Shop = shop;
        Dialogue = dialogue;
    }

    public int Id { get; }

    public string Name { get; }

    public Behaviour Behaviour { get; }

    /// <summary>How it fights, or null for a creature that cannot be attacked.</summary>
    public NpcFight? Fight { get; }

    /// <summary>What it drops when it dies, or null for nothing.</summary>
    public LootTable? Loot { get; }

    /// <summary>What it sells, or null for a creature that does not trade.</summary>
    public ShopDef? Shop { get; }

    /// <summary>The key of the conversation it opens, or null for a creature that does not talk.</summary>
    public string? Dialogue { get; }
}

/// <summary>The creatures a game has.</summary>
public sealed class NpcSet
{
    private readonly Dictionary<int, NpcDef> _byId;

    public NpcSet(IReadOnlyList<NpcDef> npcs)
    {
        Npcs = [.. npcs];
        _byId = Npcs.DistinctBy(n => n.Id).ToDictionary(n => n.Id);
        if (_byId.Count != Npcs.Count)
        {
            throw new ArgumentException("The NPCs repeat an id.", nameof(npcs));
        }
    }

    public IReadOnlyList<NpcDef> Npcs { get; }

    public NpcDef? Find(int id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// The creature called <paramref name="id"/>. Throws <see cref="ArgumentException"/> when
    /// there is none, which is how content that names a missing creature fails at startup.
    /// </summary>
    public NpcDef Require(int id) =>
        Find(id) ?? throw new ArgumentException(FormattableString.Invariant($"{id} is not an NPC."), nameof(id));
}

/// <summary>A place a creature is put, and how far from it the creature may wander.</summary>
public sealed class NpcSpawn
{
    public NpcSpawn(int npc, TileCoord tile, int radius = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        Npc = npc;
        Tile = tile;
        Radius = radius;
    }

    /// <summary>The id of the <see cref="NpcDef"/>.</summary>
    public int Npc { get; }

    public TileCoord Tile { get; }

    public int Radius { get; }
}
