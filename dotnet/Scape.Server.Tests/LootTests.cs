using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class LootTests : IAsyncDisposable
{
    private const int Rat = 1;

    private TestServer _test = Start();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static NpcSet Rats(LootTable? loot) => new([new NpcDef(Rat, "Rat", fight: new NpcFight(health: 1), loot: loot)]);

    private static LootTable Table() => new(
        always: [new ItemStack(ItemId.Bones, 1)],
        entries: [new LootItem(weight: 1, item: ItemId.Coins, min: 5, max: 9), new LootItem(weight: 1, item: ItemId.Feathers)],
        rare: new LootTable(always: [new ItemStack(ItemId.BronzeSword, 1)]),
        rareOneIn: 25);

    private static TestServer Start(LootTable? loot = null, bool low = true) =>
        new(options => options with { Npcs = Rats(loot), Random = new LootDice(low) });

    /// <summary>Starts the server again with a loot table or other dice.</summary>
    private async Task StartAsync(LootTable? loot, bool low = true)
    {
        await _test.DisposeAsync();
        _test = Start(loot, low);
    }

    /// <summary>What <paramref name="player"/> sees lying on <paramref name="tile"/>, as item to count.</summary>
    private Dictionary<int, int> SeenBy(string player, TileCoord tile) =>
        Server.Ground.LyingAt(player, tile).ToDictionary(lying => lying.Item, lying => lying.Count);

    /// <summary>Joins Ana and Bo, has Ana kill a rat beside her and returns the tile it died on.</summary>
    private async Task<TileCoord> KillAsync()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        var rat = Server.Npcs.Spawn(new NpcSpawn(Rat, At(33, 32)));
        await _test.TickAsync(2);
        await ana.AttackAsync((long)rat.NetworkId);
        await _test.TickAsync(6);
        return rat.Tile;
    }

    // A kill.

    [Fact]
    public async Task DropsWhatTheTablePaysWhereTheCreatureDied()
    {
        await StartAsync(Table());

        var tile = await KillAsync();

        Assert.Equal(new Dictionary<int, int> { [ItemId.Bones] = 1, [ItemId.BronzeSword] = 1, [ItemId.Coins] = 5 }, SeenBy("ana", tile));
    }

    [Fact]
    public async Task LeavesOutTheRareTableWhenItsDieDoesNotComeUp()
    {
        await StartAsync(Table(), low: false);

        var tile = await KillAsync();

        Assert.Equal(new Dictionary<int, int> { [ItemId.Bones] = 1, [ItemId.Feathers] = 1 }, SeenBy("ana", tile));
    }

    [Fact]
    public async Task IsForTheKillerFirst()
    {
        await StartAsync(Table());

        var tile = await KillAsync();

        Assert.Empty(SeenBy("bo", tile));
    }

    [Fact]
    public async Task IsForEveryoneOnceTheOwnerTimeEnds()
    {
        await StartAsync(Table());
        var tile = await KillAsync();

        await _test.TickAsync(GroundItems.OwnerTicks + 1);

        Assert.NotEmpty(SeenBy("bo", tile));
    }

    [Fact]
    public async Task FromACreatureWithNoTableDropsNothing()
    {
        var tile = await KillAsync();

        Assert.Empty(SeenBy("ana", tile));
    }

    [Fact]
    public void ATableThatNamesAnItemThatIsNotThereFailsAtStartup() =>
        Assert.Throws<ArgumentException>(() => Start(new LootTable(always: [new ItemStack(9999, 1)])));

    /// <summary>
    /// Dice for a one-hit kill. Every swing lands. Low dice answer every integer roll with zero,
    /// which takes the first entry and the rare table. High dice answer with the top of the range,
    /// which takes the last entry and skips the rare table.
    /// </summary>
    private sealed class LootDice(bool low) : Random
    {
        public override double NextDouble() => 0;

        public override int Next(int maxValue) => low ? 0 : maxValue - 1;
    }
}
