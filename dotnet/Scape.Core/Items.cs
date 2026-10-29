using System.Diagnostics.CodeAnalysis;

namespace Scape.Core;

/// <summary>The replicated component on an item lying on the ground.</summary>
public static class GroundItem
{
    public const string Name = "GroundItem";

    /// <summary>Ints in the component: the item id, then the count.</summary>
    public const int Ints = 2;
}

/// <summary>The skill level a player needs to use an item.</summary>
public readonly record struct Needs(string Skill, int Level);

/// <summary>One kind of item: a row of content.</summary>
public sealed class ItemDef
{
    /// <summary>The highest item id. Zero means an empty slot on the wire.</summary>
    public const int MaxId = 0xFFFF;

    /// <summary>The places a worn item may go, in the order the equipment container holds them.</summary>
    public static readonly IReadOnlyList<string> EquipSlots =
        ["head", "neck", "cape", "weapon", "body", "shield", "legs", "hands", "feet", "ring", "ammo"];

    // F2: a row of content names its fields, and most take their defaults.
    public ItemDef(
        int id,
        string name,
        bool stackable = false,
        int value = 0,
        string? equipSlot = null,
        Needs? needs = null,
        CombatBonus? combat = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (equipSlot is not null && !EquipSlots.Contains(equipSlot))
        {
            throw new ArgumentException($"{equipSlot} is not a slot.", nameof(equipSlot));
        }

        if (needs is { Level: < 1 or > SkillDef.MaxLevel })
        {
            throw new ArgumentException("The needs have a level outside range.", nameof(needs));
        }

        CheckCombat(combat, equipSlot);
        Id = id;
        Name = name;
        Stackable = stackable;
        Value = value;
        EquipSlot = equipSlot;
        Needs = needs;
        Combat = combat;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>Whether a count of this item shares one slot.</summary>
    public bool Stackable { get; }

    /// <summary>What a shop pays, in coins.</summary>
    public int Value { get; }

    /// <summary>The slot it is worn in, or null for an item that is not worn.</summary>
    public string? EquipSlot { get; }

    public Needs? Needs { get; }

    /// <summary>What it adds to a fight while worn, or null for nothing.</summary>
    public CombatBonus? Combat { get; }

    private static void CheckCombat(CombatBonus? combat, string? equipSlot)
    {
        if (combat is null)
        {
            return;
        }

        if (equipSlot is null)
        {
            throw new ArgumentException("The combat bonus is on an item not worn.", nameof(combat));
        }

        if ((combat.Speed is not null || combat.Range is not null) && equipSlot != "weapon")
        {
            throw new ArgumentException("The combat bonus gives a weapon trait to gear.", nameof(combat));
        }

        if (combat.Ammo is not null && combat.Range is null && equipSlot != "ammo")
        {
            throw new ArgumentException("The combat bonus is ammo that is not in the ammo slot.", nameof(combat));
        }
    }
}

/// <summary>
/// The items a game has. A skill an item names must exist, so a typo fails
/// when the game starts and not when a player tries the item.
/// </summary>
public sealed class ItemSet
{
    /// <summary>The first byte of a private state delta that carries container slots.</summary>
    public const int DeltaTag = 2;

    private readonly Dictionary<int, ItemDef> _byId;

    public ItemSet(IReadOnlyList<ItemDef> items, SkillSet skills)
    {
        Items = [.. items];
        _byId = Items.DistinctBy(i => i.Id).ToDictionary(i => i.Id);
        if (_byId.Count != Items.Count)
        {
            throw new ArgumentException("The items repeat an id.", nameof(items));
        }

        if (Items.FirstOrDefault(i => i.Needs is { } needs && skills.Find(needs.Skill) is null) is { } item)
        {
            throw new ArgumentException(FormattableString.Invariant($"Item {item.Id} needs an unknown skill."), nameof(items));
        }
    }

    public IReadOnlyList<ItemDef> Items { get; }

    public ItemDef? Find(int id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// The item called <paramref name="id"/>. Throws <see cref="ArgumentException"/> when there
    /// is none, which is how content that names a missing item fails at startup.
    /// </summary>
    public ItemDef Require(int id) =>
        Find(id) ?? throw new ArgumentException(FormattableString.Invariant($"{id} is not an item."), nameof(id));
}

/// <summary>A count of one kind of item in a slot.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The game's term. It is a count of one item, not a Stack<T>.")]
public readonly record struct ItemStack
{
    /// <summary>The most one stack holds. It fits a signed 32-bit integer, so a stack adds the same on every platform.</summary>
    public const int MaxCount = 0x7FFFFFFF;

    public ItemStack(int item, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(item, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item, ItemDef.MaxId);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Item = item;
        Count = count;
    }

    public int Item { get; }

    public int Count { get; }

    public override string ToString() => FormattableString.Invariant($"ItemStack({Item} x {Count})");
}
