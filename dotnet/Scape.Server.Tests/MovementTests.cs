using Orblit.Net;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class MovementTests : IAsyncDisposable
{
    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private async Task<(TestClient Client, long Avatar)> JoinAsync(string name)
    {
        var client = await _test.JoinAsync(name);
        return (client, client.Messages.OfType<Avatar>().Single().NetworkId);
    }

    private async Task<List<TileCoord?>> TilesEachTickAsync((TestClient Client, long Avatar) player, int ticks)
    {
        var seen = new List<TileCoord?>();
        for (var i = 0; i < ticks; i++)
        {
            await _test.TickAsync();
            seen.Add(player.Client.TileOf(player.Avatar));
        }

        return seen;
    }

    [Fact]
    public async Task APlayerAppearsOnTheSpawnTileAndIsToldWhichIsTheirs()
    {
        var (ana, avatar) = await JoinAsync("Ana");

        Assert.Equal(OpenWorld.SpawnTile, ana.TileOf(avatar));
        Assert.Equal(1, ana.Client.EntityCount);
    }

    [Fact]
    public async Task AWalkTakesOneStepEachTickAndEndsOnTheTarget()
    {
        var ana = await JoinAsync("Ana");

        await ana.Client.SendAsync(new Walk(At(37, 32)));
        var seen = await TilesEachTickAsync(ana, 6);

        Assert.Equal([At(33, 32), At(34, 32), At(35, 32), At(36, 32), At(37, 32), At(37, 32)], seen);
    }

    [Fact]
    public async Task AWalkGoesRoundABlockedTileAndNeverStandsOnIt()
    {
        _test.Map.SetFlags(At(33, 32), TileFlags.Blocked);
        var ana = await JoinAsync("Ana");

        await ana.Client.SendAsync(new Walk(At(34, 32)));
        var seen = await TilesEachTickAsync(ana, 4);

        // A diagonal past a blocked corner is refused, so the detour is four steps and not two.
        // Which side it takes round is the search's choice.
        Assert.Equal(At(34, 32), seen[^1]);
        Assert.All(seen.Take(3), tile => Assert.NotEqual(32, tile!.Value.Y));
    }

    [Fact]
    public async Task ANewWalkReplacesTheOneUnderWay()
    {
        var (ana, avatar) = await JoinAsync("Ana");

        await ana.SendAsync(new Walk(At(40, 32)));
        await _test.TickAsync(2);
        Assert.Equal(At(34, 32), ana.TileOf(avatar));

        await ana.SendAsync(new Walk(At(34, 36)));
        await _test.TickAsync(4);

        Assert.Equal(At(34, 36), ana.TileOf(avatar));
    }

    [Fact]
    public async Task ATargetThatCannotBeReachedEndsTheWalkBesideIt()
    {
        _test.Map.SetFlags(At(36, 32), TileFlags.Blocked);
        var (ana, avatar) = await JoinAsync("Ana");

        await ana.SendAsync(new Walk(At(36, 32)));
        await _test.TickAsync(6);

        Assert.InRange(ana.TileOf(avatar)!.Value.X, 34, 35);
    }

    [Fact]
    public async Task AWalkToAnotherPlaneIsIgnored()
    {
        var (ana, avatar) = await JoinAsync("Ana");

        await ana.SendAsync(new Walk(new TileCoord(40, 32, 1)));
        await _test.TickAsync(3);

        Assert.Equal(OpenWorld.SpawnTile, ana.TileOf(avatar));
    }

    [Fact]
    public async Task ChatAndInteractionsDoNotMoveAWalkingPlayer()
    {
        var (ana, avatar) = await JoinAsync("Ana");

        await ana.SendAsync(new Walk(At(40, 32)));
        await _test.TickAsync();
        await ana.SendAsync(new Chat("hello"));
        await _test.TickAsync();

        Assert.Equal(At(34, 32), ana.TileOf(avatar));
    }

    [Fact]
    public async Task TwoPlayersWalkWithoutDisturbingEachOther()
    {
        var (ana, anaId) = await JoinAsync("Ana");
        var (bo, boId) = await JoinAsync("Bo");

        await ana.SendAsync(new Walk(At(36, 32)));
        await bo.SendAsync(new Walk(At(32, 36)));
        await _test.TickAsync(4);

        Assert.Equal(At(36, 32), ana.TileOf(anaId));
        Assert.Equal(At(32, 36), bo.TileOf(boId));
    }

    [Fact]
    public async Task APlayerWhoIsGoneLeavesTheWorldAfterTheLingerTime()
    {
        var (ana, avatar) = await JoinAsync("Ana");
        await JoinAsync("Bo");
        await ana.Link.CloseAsync();

        await _test.TickAsync(11);

        Assert.Null(Server.Host.EntityFor((ulong)avatar));
    }

    [Fact]
    public async Task AnAvatarNamesItsPlayerOnlyWhileThePlayerIsIn()
    {
        var (ana, avatar) = await JoinAsync("Ana");
        await JoinAsync("Bo");
        Assert.Equal("ana", Server.Movement.PlayerWith((ulong)avatar));
        await ana.Link.CloseAsync();

        await _test.TickAsync(11);

        Assert.Null(Server.Movement.PlayerWith((ulong)avatar));
    }

    [Fact]
    public void ASpawnTileThatIsNotOpenGroundIsRefused()
    {
        var map = OpenWorld.Map();
        map.SetFlags(OpenWorld.SpawnTile, TileFlags.Blocked);

        Assert.Throws<ArgumentException>(() => new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(GameTick.Period, new FakeTime().Read),
            Pacer = new NoPacer(),
            Log = _ => { },
            Map = map,
            Spawn = OpenWorld.SpawnTile,
            Items = DefaultItems.All,
            Objects = DefaultObjects.All,
            Npcs = DefaultNpcs.All,
        }));
    }
}
