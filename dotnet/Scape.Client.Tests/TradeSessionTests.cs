using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class TradeSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private Container Bag() => _game.Server.Items.ContainerOf("ana", ContainerId.Inventory)!;

    private async Task<GameSession> ShopperAsync()
    {
        var ana = await _game.JoinAsync("Ana");
        var keeper = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(33, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);
        ana.Trade(keeper);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        return ana;
    }

    [Fact]
    public async Task TradingOpensTheShopAndTheSessionHoldsItsStock()
    {
        var ana = await ShopperAsync();

        var axe = ana.Shop!.Lines.First(line => line.Item == ItemId.BronzeAxe);

        Assert.Equal(5, axe.Count);
        Assert.False(ana.IsBankOpen);
    }

    [Fact]
    public async Task APurchaseChangesTheInventoryAndTheStockTheSessionSees()
    {
        var ana = await ShopperAsync();
        Bag().Add(ItemId.Coins, 100);

        ana.Buy(ItemId.BronzeAxe, 1);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);

        Assert.Equal(ItemId.BronzeAxe, ana.ItemAt(ContainerId.Inventory, 1)?.Item);
        Assert.Equal(4, ana.Shop!.Lines[0].Count);
    }

    [Fact]
    public async Task ASalePaysCoins()
    {
        var ana = await ShopperAsync();
        Bag().Add(ItemId.Logs, 2);
        await _game.TickAndDrawAsync(ana);

        ana.Sell(ItemId.Logs, 2);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);

        Assert.Equal(2, Bag().CountOf(ItemId.Coins));
        Assert.Equal(ItemId.Coins, ana.ItemAt(ContainerId.Inventory, 0)?.Item);
    }

    [Fact]
    public async Task WalkingAwayClosesTheShop()
    {
        var ana = await ShopperAsync();

        ana.WalkTo(new TileCoord(60, 60, 0));
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 12);

        Assert.Null(ana.Shop);
    }

    [Fact]
    public async Task TheBankOpensTakesADepositAndGivesItBack()
    {
        var ana = await _game.JoinAsync("Ana");
        var booth = _game.Server.Objects.Place(ObjectId.BankBooth, new TileCoord(33, 32, 0));
        Bag().Add(ItemId.Feathers, 8);
        await _game.TickAndDrawAsync(ana, 2);

        ana.UseObject((long)booth.NetworkId, 0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        Assert.True(ana.IsBankOpen);

        ana.Deposit(0, 8);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);
        Assert.Equal(new ItemStack(ItemId.Feathers, 8), ana.ItemAt(ContainerId.Bank, 0));
        Assert.Null(ana.ItemAt(ContainerId.Inventory, 0));

        ana.Withdraw(0, 3);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);
        Assert.Equal(new ItemStack(ItemId.Feathers, 3), ana.ItemAt(ContainerId.Inventory, 0));
    }

    [Fact]
    public async Task OpeningAShopClosesTheBankOnTheClient()
    {
        var ana = await _game.JoinAsync("Ana");
        var booth = _game.Server.Objects.Place(ObjectId.BankBooth, new TileCoord(33, 32, 0));
        var keeper = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(31, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);
        ana.UseObject((long)booth.NetworkId, 0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        Assert.True(ana.IsBankOpen);

        ana.Trade(keeper);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 8);

        Assert.NotNull(ana.Shop);
        Assert.False(ana.IsBankOpen);
    }

    [Fact]
    public async Task StartingAConversationClosesTheBankOnTheClient()
    {
        var ana = await _game.JoinAsync("Ana");
        var booth = _game.Server.Objects.Place(ObjectId.BankBooth, new TileCoord(33, 32, 0));
        var keeper = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(31, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);
        ana.UseObject((long)booth.NetworkId, 0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        Assert.True(ana.IsBankOpen);

        ana.Talk(keeper);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 8);

        Assert.NotNull(ana.Line);
        Assert.False(ana.IsBankOpen);
    }

    [Fact]
    public async Task OpeningTheBankClosesAConversationOnTheClient()
    {
        var ana = await _game.JoinAsync("Ana");
        var booth = _game.Server.Objects.Place(ObjectId.BankBooth, new TileCoord(33, 32, 0));
        var keeper = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(31, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);
        ana.Talk(keeper);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        Assert.NotNull(ana.Line);

        ana.UseObject((long)booth.NetworkId, 0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 8);

        Assert.True(ana.IsBankOpen);
        Assert.Null(ana.Line);
    }
}
