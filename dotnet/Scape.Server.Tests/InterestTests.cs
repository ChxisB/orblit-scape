using Scape.Core;

namespace Scape.Server.Tests;

public sealed class InterestTests : IAsyncDisposable
{
    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private int PositionBytes(string name) =>
        Server.Host.Summary().BytesSent[Wire.NameKey(name)].Columns[TilePosition.Name];

    // Interest.

    [Fact]
    public async Task PlayersOnTheSameGroundSeeEachOther()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await _test.TickAsync();

        Assert.Equal(2, ana.Client.EntityCount);
        Assert.Equal(2, bo.Client.EntityCount);
    }

    [Fact]
    public async Task APlayerWhoWalksOutOfTheRingIsRemovedFromView()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await bo.SendAsync(new Walk(At(100, 32)));

        await _test.TickAsync(80);

        Assert.Equal(1, ana.Client.EntityCount);
        Assert.Equal(1, bo.Client.EntityCount);
    }

    [Fact]
    public async Task APlayerFarAwayIsSentNoneOfTheMovement()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        var cy = await _test.JoinAsync("Cy");
        await cy.SendAsync(new Walk(At(110, 32)));
        await _test.TickAsync(80);
        var boBefore = PositionBytes("Bo");
        var cyBefore = PositionBytes("Cy");

        await ana.SendAsync(new Walk(At(37, 32)));
        await _test.TickAsync(6);

        Assert.True(PositionBytes("Bo") > boBefore);
        Assert.Equal(cyBefore, PositionBytes("Cy"));
        Assert.Equal(2, bo.Client.EntityCount);
        Assert.Equal(1, cy.Client.EntityCount);
    }

    [Fact]
    public async Task ADroppedPlayerLeavesTheOthersViewAfterTheLinger()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await _test.TickAsync();
        await bo.Link.CloseAsync();

        await _test.TickAsync(9);
        Assert.Equal(2, ana.Client.EntityCount);

        await _test.TickAsync(2);
        Assert.Equal(1, ana.Client.EntityCount);
    }

    // Active cells.

    [Fact]
    public async Task CellsNearAPlayerAreActiveAndTheRestAreNot()
    {
        await _test.JoinAsync("Ana");
        await _test.TickAsync();

        Assert.True(Server.Active.IsActiveAt(32, 32));
        Assert.True(Server.Active.IsActiveAt(60, 60));
        Assert.False(Server.Active.IsActiveAt(100, 100));
    }

    [Fact]
    public async Task ACellStaysActiveForAWhileAfterThePlayerLeaves()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(new Walk(At(100, 32)));

        // At x 66 the player is two cells from the spawn cell.
        await _test.TickAsync(34);
        Assert.True(Server.Active.IsActiveAt(32, 32));

        await _test.TickAsync(12);

        Assert.False(Server.Active.IsActiveAt(32, 32));
        Assert.True(Server.Active.IsActiveAt(80, 32));
    }
}
