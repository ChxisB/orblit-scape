using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class LootTests
{
    private const int Rolls = 20000;

    private static Dictionary<int, int> Sample(LootTable table, int seed = 7)
    {
        var random = new Random(seed);
        var totals = new Dictionary<int, int>();
        for (var i = 0; i < Rolls; i++)
        {
            foreach (var stack in table.Roll(random))
            {
                totals[stack.Item] = totals.GetValueOrDefault(stack.Item) + stack.Count;
            }
        }

        return totals;
    }

    // Rolling.

    [Fact]
    public void AlwaysPaysWhatItAlwaysPays()
    {
        var table = new LootTable(always: [new ItemStack(Logs, 2)]);

        Assert.Equal([new ItemStack(Logs, 2)], table.Roll(new Random(1)));
    }

    [Fact]
    public void AnEmptyTablePaysNothing()
    {
        Assert.Empty(new LootTable().Roll(new Random(1)));
    }

    [Fact]
    public void PicksTheEntryTheRollLandsIn()
    {
        var table = new LootTable(entries: [new LootItem(3, Logs), new LootItem(1, Coins)]);

        Assert.Equal([new ItemStack(Logs, 1)], table.Roll(new Fixed(2)));
        Assert.Equal([new ItemStack(Coins, 1)], table.Roll(new Fixed(3)));
    }

    [Fact]
    public void TheWeightOfNothingIsARollThatPaysNoEntry()
    {
        var table = new LootTable(entries: [new LootItem(1, Logs)], nothing: 2);

        Assert.Equal([new ItemStack(Logs, 1)], table.Roll(new Fixed(0)));
        Assert.Empty(table.Roll(new Fixed(1)));
        Assert.Empty(table.Roll(new Fixed(2)));
    }

    [Fact]
    public void ACountRunsFromItsFewestToItsMost()
    {
        var table = new LootTable(entries: [new LootItem(1, Coins, min: 5, max: 9)]);

        Assert.Equal(5, Assert.Single(table.Roll(new Fixed(0, 0))).Count);
        Assert.Equal(9, Assert.Single(table.Roll(new Fixed(0, 4))).Count);
    }

    [Fact]
    public void AnEntryCanBeAnotherTableWhichRollsWhole()
    {
        var inner = new LootTable(always: [new ItemStack(Logs, 1)], entries: [new LootItem(1, Coins)]);
        var table = new LootTable(entries: [new LootSub(1, inner)]);

        Assert.Equal([new ItemStack(Logs, 1), new ItemStack(Coins, 1)], table.Roll(new Fixed(0)));
    }

    [Fact]
    public void TheRareTablePaysOnceInSoManyRollsAndOnTopOfThePick()
    {
        var table = new LootTable(
            entries: [new LootItem(1, Logs)],
            rare: new LootTable(always: [new ItemStack(Sword, 1)]),
            rareOneIn: 50);

        Assert.Equal([new ItemStack(Sword, 1), new ItemStack(Logs, 1)], table.Roll(new Fixed(0)));
        Assert.Equal([new ItemStack(Logs, 1)], table.Roll(new Fixed(1)));
    }

    // Over many rolls.

    [Fact]
    public void WhatIsPaidConvergesOnTheWeights()
    {
        var table = new LootTable(entries: [new LootItem(6, Logs), new LootItem(3, Coins), new LootItem(1, Sword)]);

        var totals = Sample(table);

        Assert.Equal(0.6, (double)totals[Logs] / Rolls, 0.02);
        Assert.Equal(0.3, (double)totals[Coins] / Rolls, 0.02);
        Assert.Equal(0.1, (double)totals[Sword] / Rolls, 0.02);
    }

    [Fact]
    public void TheRareTierComesUpAboutOnceInRareOneInRolls()
    {
        var table = new LootTable(rare: new LootTable(always: [new ItemStack(Sword, 1)]), rareOneIn: 20);

        Assert.Equal(0.05, (double)Sample(table)[Sword] / Rolls, 0.01);
    }

    [Fact]
    public void NothingTakesItsShareOfTheRolls()
    {
        var table = new LootTable(entries: [new LootItem(1, Logs)], nothing: 3);

        Assert.Equal(0.25, (double)Sample(table)[Logs] / Rolls, 0.02);
    }

    // Items.

    [Fact]
    public void ListsEveryItemTheTableCanPayTablesInsideTablesToo()
    {
        var table = new LootTable(
            always: [new ItemStack(Logs, 1)],
            entries: [new LootItem(1, Coins), new LootSub(1, new LootTable(entries: [new LootItem(1, Helmet)]))],
            rare: new LootTable(always: [new ItemStack(Sword, 1)]),
            rareOneIn: 10);

        Assert.Equal(new HashSet<int> { Logs, Coins, Helmet, Sword }, table.Items.ToHashSet());
    }

    // Building.

    [Fact]
    public void RefusesAWeightBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LootItem(0, Logs));
    }

    [Fact]
    public void RefusesACountBelowOneOrAMostBelowTheFewest()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LootItem(1, Logs, min: 0));
        Assert.ThrowsAny<ArgumentException>(() => new LootItem(1, Logs, min: 3, max: 2));
    }

    [Fact]
    public void RefusesARareTierThatAlwaysPaysOrNeverDoes()
    {
        var rare = new LootTable();

        Assert.ThrowsAny<ArgumentException>(() => new LootTable(rare: rare, rareOneIn: 1));
        Assert.ThrowsAny<ArgumentException>(() => new LootTable(rare: rare));
        Assert.ThrowsAny<ArgumentException>(() => new LootTable(rareOneIn: 5));
    }

    [Fact]
    public void RefusesANegativeWeightOfNothing()
    {
        Assert.ThrowsAny<ArgumentException>(() => new LootTable(nothing: -1));
    }

    /// <summary>Gives the numbers it was made with, in order, and repeats the last.</summary>
    private sealed class Fixed(params int[] values) : Random
    {
        private int _at;

        public override int Next(int maxValue) => values[_at < values.Length - 1 ? _at++ : _at] % maxValue;

        public override double NextDouble() => 0;
    }
}
