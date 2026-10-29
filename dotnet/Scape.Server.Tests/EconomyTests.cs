using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class EconomyTests : IAsyncDisposable
{
    private const int Within = 120;

    private static readonly NodeSet Trees = new(
        [
            new NodeDef(
                ObjectId.Tree,
                "woodcutting",
                level: 1,
                tool: ItemId.BronzeAxe,
                xp: 25,
                product: ItemId.Logs,
                chanceLow: 0.3,
                chanceHigh: 0.7,
                fullLevel: 30,
                depleteChance: 0,
                respawnTicks: 25),
        ],
        DefaultObjects.All,
        DefaultItems.All,
        DefaultSkills.All);

    private readonly TestServer _test = new(options => options with { Nodes = Trees, Random = new Lucky() });

    private ScapeServer Server => _test.Server;

    private Container Inventory => Server.Items.ContainerOf("ana", ContainerId.Inventory)!;

    private Container Bank => Server.Items.ContainerOf("ana", ContainerId.Bank)!;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    /// <summary>
    /// Steps the world until <paramref name="done"/>, so a test says what it waits for and not
    /// how many ticks the walking takes.
    /// </summary>
    private async Task UntilAsync(Func<bool> done)
    {
        for (var i = 0; i < Within && !done(); i++)
        {
            await _test.TickAsync();
        }

        Assert.True(done(), $"It did not happen within {Within} ticks.");
    }

    private async Task PickUpAllAsync(TestClient ana, TileCoord tile)
    {
        foreach (var lying in Server.Ground.LyingAt("ana", tile))
        {
            var held = Inventory.CountOf(lying.Item);
            await ana.ClickAsync(InteractKind.Ground, (long)lying.NetworkId);
            await UntilAsync(() => Inventory.CountOf(lying.Item) > held);
        }
    }

    private Task DepositAsync(TestClient ana, int item)
    {
        var slot = Inventory.Occupied().First(slot => Inventory.At(slot)?.Item == item);
        return ana.SendAsync(new Deposit(slot, Inventory.CountOf(item), tab: 0));
    }

    // Milestone B: a player can make a living in the world.
    [Fact]
    public async Task OnePlayerGathersFightsLootsSellsAndBanks()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.TickAsync();
        Inventory.Add(ItemId.BronzeAxe, 1);
        var tree = Server.Objects.Place(ObjectId.Tree, At(33, 32));
        var chicken = Server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, At(31, 32)));
        var keeper = Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, At(32, 36)));
        var booth = Server.Objects.Place(ObjectId.BankBooth, At(36, 36));

        await ana.ClickAsync(InteractKind.PlacedObject, (long)tree.NetworkId);
        await UntilAsync(() => Inventory.CountOf(ItemId.Logs) >= 3);
        Assert.True(Server.Skills.XpOf("ana", "woodcutting") > 0);

        await ana.AttackAsync((long)chicken.NetworkId);
        await UntilAsync(() => Server.Ground.LyingAt("ana", chicken.Tile).Count == 2);
        Assert.True(Server.Skills.XpOf("ana", "fighting") > 0);
        await PickUpAllAsync(ana, chicken.Tile);
        Assert.Equal(1, Inventory.CountOf(ItemId.Bones));
        Assert.Equal(1, Inventory.CountOf(ItemId.Feathers));

        await ana.ClickAsync(InteractKind.Npc, (long)keeper.NetworkId, NpcOption.Trade);
        await UntilAsync(() => ana.Messages.Any(message => message is ShopView));
        await ana.SendAsync(new Sell(ItemId.Logs, 3));
        await _test.TickAsync(2);
        Assert.Equal(0, Inventory.CountOf(ItemId.Logs));
        Assert.Equal(3, Inventory.CountOf(ItemId.Coins));

        await ana.ClickAsync(InteractKind.PlacedObject, (long)booth.NetworkId);
        await UntilAsync(() => ana.Messages.Any(message => message is BankOpen));
        await DepositAsync(ana, ItemId.Coins);
        await DepositAsync(ana, ItemId.Feathers);
        await _test.TickAsync(2);

        Assert.Equal(3, Bank.CountOf(ItemId.Coins));
        Assert.Equal(1, Bank.CountOf(ItemId.Feathers));
        Assert.Equal(1, Inventory.CountOf(ItemId.Bones));
        Assert.Equal(1, Inventory.CountOf(ItemId.BronzeAxe));
    }

    /// <summary>
    /// Every try works, every swing lands for one, and every loot roll takes the first entry. A
    /// tree that never runs dry gives logs for as long as it is worked.
    /// </summary>
    private sealed class Lucky : Random
    {
        public override double NextDouble() => 0;

        public override int Next(int maxValue) => 0;
    }
}
