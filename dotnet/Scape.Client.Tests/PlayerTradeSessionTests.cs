using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class PlayerTradeSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private async Task<(GameSession Ana, GameSession Bo)> TradingAsync()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        bo.WalkTo(new TileCoord(33, 32, 0));
        await _game.SettleAsync(3);
        _game.Server.Items.ContainerOf("ana", ContainerId.Inventory)!.Add(ItemId.Logs, 3);
        ana.RequestTrade(InProcessGame.AvatarOf(bo));
        await _game.SettleAsync(3);
        bo.RequestTrade(InProcessGame.AvatarOf(ana));
        await _game.SettleAsync(3);
        return (ana, bo);
    }

    [Fact]
    public async Task ATradeOpensForBothAndShowsWhatTheOtherOffers()
    {
        var (ana, bo) = await TradingAsync();
        Assert.Equal("bo", ana.PlayerTrade!.Partner);
        Assert.Equal("ana", bo.PlayerTrade!.Partner);

        ana.Offer(ItemId.Logs, 2);
        await _game.SettleAsync(3);

        Assert.Equal([new ItemStack(ItemId.Logs, 2)], bo.PlayerTrade!.Theirs);
        Assert.Equal([new ItemStack(ItemId.Logs, 2)], ana.PlayerTrade!.Mine);
    }

    [Fact]
    public async Task DecliningClosesTheTradeForBoth()
    {
        var (ana, bo) = await TradingAsync();

        bo.DeclineTrade();
        await _game.SettleAsync(3);

        Assert.Null(ana.PlayerTrade);
        Assert.Null(bo.PlayerTrade);
    }

    [Fact]
    public async Task AShopInItsPlaceClosesTheTrade()
    {
        var (ana, _) = await TradingAsync();
        var keeper = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(31, 32, 0))).NetworkId;
        await _game.SettleAsync(3);

        ana.Trade(keeper);
        await _game.SettleAsync(8);

        Assert.Null(ana.PlayerTrade);
        Assert.NotNull(ana.Shop);
    }
}
