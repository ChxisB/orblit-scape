using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class ShopsTests : IAsyncDisposable
{
    // The default prices: an axe is worth 30, so it sells for 39 and buys for 12.
    private const int AxeBuy = 39;
    private const int AxeSell = 12;

    private TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private Container Inventory(string key) => Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    private PlacedNpc Keeper() => Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, At(33, 32)));

    private static Task TradeAsync(TestClient client, PlacedNpc npc) =>
        client.ClickAsync(InteractKind.Npc, (long)npc.NetworkId, NpcOption.Trade);

    /// <summary>Joins <paramref name="name"/> with <paramref name="coins"/> and has them open the shop of <paramref name="npc"/>.</summary>
    private async Task<TestClient> ShopperAsync(PlacedNpc npc, string name = "Ana", long coins = 0)
    {
        var player = await _test.JoinAsync(name);
        if (coins > 0)
        {
            Inventory(name.ToLowerInvariant()).Add(ItemId.Coins, coins);
        }

        await _test.TickAsync(2);
        await TradeAsync(player, npc);
        await _test.TickAsync(4);
        return player;
    }

    private static void FillWithBones(Container bag)
    {
        while (bag.Occupied().Count < Container.InventorySize)
        {
            bag.Add(ItemId.Bones, 1);
        }
    }

    private static ShopLine FirstKeeperLine => DefaultNpcs.All.Find(NpcId.Shopkeeper)!.Shop!.Lines[0];

    /// <summary>A creature for each of <paramref name="lines"/>, with ids from 9 up, that sells only that line.</summary>
    private static NpcSet ShopsOf(params ShopLine[] lines) =>
        new([.. lines.Select((line, i) => new NpcDef(9 + i, "Plumer", shop: new ShopDef([line])))]);

    // Opening.

    [Fact]
    public async Task TradingWalksUpAndSendsTheStock()
    {
        var ana = await ShopperAsync(Keeper());

        Assert.NotEmpty(ana.Views);
        var lines = ana.Views[^1].Lines;
        Assert.Contains(ItemId.BronzeAxe, lines.Select(line => line.Item));
        Assert.Equal(5, lines.First(line => line.Item == ItemId.BronzeAxe).Count);
    }

    [Fact]
    public async Task ALineThatStartsEmptyIsListedAtZero()
    {
        var ana = await ShopperAsync(Keeper());

        Assert.Equal(0, ana.Views[^1].Lines.First(line => line.Item == ItemId.Logs).Count);
    }

    [Fact]
    public async Task ACreatureWithNoShopHasNothingToSell()
    {
        var ana = await _test.JoinAsync("Ana");
        var chicken = Server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, At(33, 32)));
        await _test.TickAsync(2);

        await TradeAsync(ana, chicken);
        await _test.TickAsync(4);

        Assert.Empty(ana.Views);
    }

    [Fact]
    public async Task ARequestWithNoWindowOpenDoesNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 100);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(100, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task ARequestWithTheBankOpenDoesNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        var booth = Server.Objects.Place(ObjectId.BankBooth, At(33, 32));
        Inventory("ana").Add(ItemId.Coins, 100);
        await _test.TickAsync(2);
        await ana.ClickAsync(InteractKind.PlacedObject, (long)booth.NetworkId);
        await _test.TickAsync(4);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Single(ana.Messages.OfType<BankOpen>());
        Assert.DoesNotContain("They do not sell that.", ana.Notices);
    }

    // Buying.

    [Fact]
    public async Task BuyingTakesThePriceAndGivesTheItem()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);

        Assert.Equal(2, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(100 - 2 * AxeBuy, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task BuyingWithExactlyThePriceSpendsEveryCoin()
    {
        var ana = await ShopperAsync(Keeper(), coins: AxeBuy);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task BuyingWithTooFewCoinsChangesNothing()
    {
        var ana = await ShopperAsync(Keeper(), coins: AxeBuy - 1);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Contains("You cannot afford that.", ana.Notices);
        Assert.Equal(AxeBuy - 1, Inventory("ana").CountOf(ItemId.Coins));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(5, ana.Views[^1].Lines[0].Count);
    }

    [Fact]
    public async Task BuyingWithNoRoomChangesNothing()
    {
        var ana = await ShopperAsync(Keeper(), coins: 1000);
        var bag = Inventory("ana");
        FillWithBones(bag);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Contains("You have no room for that.", ana.Notices);
        Assert.Equal(1000, bag.CountOf(ItemId.Coins));
        Assert.Equal(0, bag.CountOf(ItemId.BronzeAxe));
    }

    [Fact]
    public async Task BuyingIsLimitedByTheStock()
    {
        var ana = await ShopperAsync(Keeper(), coins: 1000);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 6));
        await _test.TickAsync(2);

        Assert.Contains("They have not got that many.", ana.Notices);
        Assert.Equal(1000, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task BuyingAnItemTheShopDoesNotStockIsRefused()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);

        await ana.SendAsync(new Buy(ItemId.BronzeBar, 1));
        await _test.TickAsync(2);

        Assert.Contains("They do not sell that.", ana.Notices);
        Assert.Equal(100, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task StockIsSharedSoOnePlayerBuyingEmptiesItForAnother()
    {
        var npc = Keeper();
        var first = await ShopperAsync(npc, coins: 1000);
        var bob = await ShopperAsync(npc, "Bob", 1000);

        await first.SendAsync(new Buy(ItemId.BronzeAxe, 5));
        await _test.TickAsync(2);
        await bob.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Contains("They have not got that many.", bob.Notices);
        Assert.Equal(1000, Inventory("bob").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task BuyingTellsEveryoneWhoIsLooking()
    {
        var npc = Keeper();
        var ana = await ShopperAsync(npc, coins: 100);
        var bob = await ShopperAsync(npc, "Bob");
        var before = bob.Views.Count;

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.True(bob.Views.Count > before);
        Assert.Equal(4, bob.Views[^1].Lines.First(line => line.Item == ItemId.BronzeAxe).Count);
    }

    [Fact]
    public async Task BuyingTellsNobodyLookingAtAnotherShop()
    {
        await _test.DisposeAsync();
        var feathers = new ShopLine(ItemId.Feathers, 10);
        _test = new TestServer(options => options with { Npcs = ShopsOf(feathers, feathers) });
        var ana = await ShopperAsync(Server.Npcs.Spawn(new NpcSpawn(9, At(33, 32))), coins: 40);
        var bob = await ShopperAsync(Server.Npcs.Spawn(new NpcSpawn(10, At(31, 32))), "Bob");

        await ana.SendAsync(new Buy(ItemId.Feathers, 1));
        await _test.TickAsync(2);

        Assert.NotEmpty(bob.Views);
        Assert.All(bob.Views, view => Assert.Equal(10, view.Npc));
    }

    [Fact]
    public async Task AViewGoesOutOnlyOnATickWhenTheStockChanged()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);
        var before = ana.Views.Count;

        await _test.TickAsync(5);

        Assert.Equal(before, ana.Views.Count);
    }

    // Selling.

    [Fact]
    public async Task SellingTakesTheItemAndPaysThePrice()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);

        await ana.SendAsync(new Sell(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);

        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(100 - 2 * AxeBuy + 2 * AxeSell, Inventory("ana").CountOf(ItemId.Coins));
        Assert.Equal(5, ana.Views[^1].Lines[0].Count);
    }

    [Fact]
    public async Task SellingIsCappedByTheRoomTheShopHas()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);
        Inventory("ana").Add(ItemId.BronzeAxe, 2);

        await ana.SendAsync(new Sell(ItemId.BronzeAxe, 3));
        await _test.TickAsync(2);

        Assert.Equal(2, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(100 - AxeBuy + AxeSell, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task AFullShopTakesNothing()
    {
        var ana = await ShopperAsync(Keeper());
        Inventory("ana").Add(ItemId.BronzeAxe, 1);

        await ana.SendAsync(new Sell(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Contains("They will not take any of that.", ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BronzeAxe));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Coins));
    }

    [Fact]
    public async Task SellingAnItemTheShopDoesNotStockIsRefused()
    {
        var ana = await ShopperAsync(Keeper());
        Inventory("ana").Add(ItemId.BronzeBar, 1);

        await ana.SendAsync(new Sell(ItemId.BronzeBar, 1));
        await _test.TickAsync(2);

        Assert.Contains("They do not buy that.", ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BronzeBar));
    }

    [Fact]
    public async Task SellingWithNoRoomForTheCoinsChangesNothing()
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Npcs = ShopsOf(new ShopLine(ItemId.Feathers, 10)) });
        var npc = Server.Npcs.Spawn(new NpcSpawn(9, At(33, 32)));
        var ana = await ShopperAsync(npc, coins: 40);
        await ana.SendAsync(new Buy(ItemId.Feathers, 5));
        await _test.TickAsync(2);
        var bag = Inventory("ana");
        bag.Take(bag.Search("coins").Single());
        FillWithBones(bag);

        await ana.SendAsync(new Sell(ItemId.Feathers, 2));
        await _test.TickAsync(2);

        Assert.Contains("You have no room for the coins.", ana.Notices);
        Assert.Equal(5, bag.CountOf(ItemId.Feathers));
    }

    // Restocking.

    [Fact]
    public async Task RestockingBringsOneItemBackEachIntervalAndNeverAboveTheCap()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);

        await _test.TickAsync(FirstKeeperLine.RestockTicks * 5);

        Assert.Equal(FirstKeeperLine.Cap, ana.Views[^1].Lines[0].Count);
    }

    [Fact]
    public async Task RestockingDoesNotAddAnItemBeforeTheIntervalEnds()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);

        await _test.TickAsync(FirstKeeperLine.RestockTicks - 5);

        Assert.Equal(FirstKeeperLine.Cap - 2, ana.Views[^1].Lines[0].Count);
    }

    [Fact]
    public async Task RestockingAddsTheNextItemOneIntervalAfterTheLast()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 2));
        await _test.TickAsync(2);
        for (var i = 0; i < FirstKeeperLine.RestockTicks && ana.Views[^1].Lines[0].Count < FirstKeeperLine.Cap - 1; i++)
        {
            await _test.TickAsync();
        }

        await _test.TickAsync(FirstKeeperLine.RestockTicks - 1);
        Assert.Equal(FirstKeeperLine.Cap - 1, ana.Views[^1].Lines[0].Count);

        await _test.TickAsync();
        Assert.Equal(FirstKeeperLine.Cap, ana.Views[^1].Lines[0].Count);
    }

    // The window.

    [Fact]
    public async Task TheWindowClosesWhenThePlayerWalksAway()
    {
        var ana = await ShopperAsync(Keeper());
        Assert.Empty(ana.Messages.OfType<WindowClosed>());

        await ana.SendAsync(new Walk(At(60, 60)));
        await _test.TickAsync(12);

        Assert.NotEmpty(ana.Messages.OfType<WindowClosed>());
    }

    [Fact]
    public async Task AClosedWindowKeepsARequestFromWorking()
    {
        var ana = await ShopperAsync(Keeper(), coins: 100);
        await ana.SendAsync(new Walk(At(60, 60)));
        await _test.TickAsync(12);

        await ana.SendAsync(new Buy(ItemId.BronzeAxe, 1));
        await _test.TickAsync(2);

        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeAxe));
    }

    [Fact]
    public void ACreatureWithAShopAndNoRulesFailsAtStartup() =>
        Assert.Throws<ArgumentException>(() => new TestServer(options => options with { Shops = null }));

    [Fact]
    public void AShopThatStocksCoinsFailsAtStartup() =>
        Assert.Throws<ArgumentException>(() => new TestServer(options => options with { Npcs = ShopsOf(new ShopLine(ItemId.Coins, 10)) }));

    [Fact]
    public void ALineDearerThanAStackOfCoinsFailsAtStartup() =>
        Assert.Throws<ArgumentException>(() => new TestServer(options => options with
        {
            Npcs = ShopsOf(new ShopLine(ItemId.BronzeAxe, ShopLine.MaxStock)),
            Shops = new ShopRules(ItemId.Coins, buyPercent: 10000),
        }));
}

/// <summary>What a client was shown of shops.</summary>
file static class ShopReading
{
    extension(TestClient client)
    {
        public IReadOnlyList<ShopView> Views => [.. client.Messages.OfType<ShopView>()];
    }
}
