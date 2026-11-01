namespace Scape.Core;

/// <summary>
/// One way a table can pay out. The chance of picking it is its <see cref="Weight"/>
/// over the weights of the whole table.
/// </summary>
public abstract class LootEntry
{
    // Closed to this assembly, so the cases below are every entry.
    private protected LootEntry(int weight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        Weight = weight;
    }

    public int Weight { get; }
}

/// <summary>Between <see cref="Min"/> and <see cref="Max"/> of one item.</summary>
public sealed class LootItem : LootEntry
{
    public LootItem(int weight, int item, int min = 1, int? max = null)
        : base(weight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(item, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item, ItemDef.MaxId);
        Max = max ?? min;
        if (min < 1 || Max < min)
        {
            throw new ArgumentException("A count is at least one and max is not below min.");
        }

        Item = item;
        Min = min;
    }

    public int Item { get; }

    public int Min { get; }

    public int Max { get; }
}

/// <summary>Whatever another table pays out.</summary>
public sealed class LootSub(int weight, LootTable table) : LootEntry(weight)
{
    public LootTable Table { get; } = table;
}

/// <summary>
/// What a kill pays. It always gives <see cref="Always"/>, gives the <see cref="Rare"/> table
/// once in <see cref="RareOneIn"/> rolls, and picks one of <see cref="Entries"/>.
/// <see cref="Nothing"/> is the weight of picking none of them. The dice are the
/// caller's, so a test can fix them.
/// </summary>
public sealed class LootTable
{
    private readonly int _total;

    // F2: a row of content names its fields, and most take their defaults.
    public LootTable(
        IReadOnlyList<ItemStack>? always = null,
        IReadOnlyList<LootEntry>? entries = null,
        int nothing = 0,
        LootTable? rare = null,
        int rareOneIn = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nothing);
        if (rare is null ? rareOneIn != 0 : rareOneIn < 2)
        {
            throw new ArgumentException("A rare table needs one in 2 or more, and no rare table needs zero.", nameof(rareOneIn));
        }

        Always = [.. always ?? []];
        Entries = [.. entries ?? []];
        Nothing = nothing;
        Rare = rare;
        RareOneIn = rareOneIn;
        _total = nothing + Entries.Sum(e => e.Weight);
    }

    public IReadOnlyList<ItemStack> Always { get; }

    public IReadOnlyList<LootEntry> Entries { get; }

    public int Nothing { get; }

    public LootTable? Rare { get; }

    public int RareOneIn { get; }

    /// <summary>Every item the table can pay out, so content can check each exists.</summary>
    public IReadOnlyList<int> Items =>
    [
        .. Always.Select(s => s.Item),
        .. Entries.SelectMany(ItemsOf),
        .. Rare?.Items ?? [],
    ];

    /// <summary>One roll of the table.</summary>
    // F5: a roll answers with what it paid and spends the dice it is given.
    public IReadOnlyList<ItemStack> Roll(Random random)
    {
        List<ItemStack> paid = [.. Always];
        if (Rare is { } rare && random.Next(RareOneIn) == 0)
        {
            paid.AddRange(rare.Roll(random));
        }

        switch (Pick(random))
        {
            case LootItem item:
                paid.Add(new ItemStack(item.Item, item.Min + random.Next(item.Max - item.Min + 1)));
                break;
            case LootSub sub:
                paid.AddRange(sub.Table.Roll(random));
                break;
        }

        return paid;
    }

    private static IReadOnlyList<int> ItemsOf(LootEntry entry) => entry switch
    {
        LootItem item => [item.Item],
        LootSub sub => sub.Table.Items,
        _ => throw new InvalidOperationException($"{entry.GetType().Name} is not a loot entry."),
    };

    private LootEntry? Pick(Random random)
    {
        if (_total == 0)
        {
            return null;
        }

        var at = random.Next(_total);
        foreach (var entry in Entries)
        {
            if (at < entry.Weight)
            {
                return entry;
            }

            at -= entry.Weight;
        }

        return null;
    }
}
