using Scape.Content;
using Scape.Core;
using Scape.Server;

namespace Scape.Bots.Tests;

public sealed class BotsTests : IAsyncDisposable
{
    private readonly List<BotRun> _runs = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var run in _runs)
        {
            await run.DisposeAsync();
        }
    }

    private static TileCoord At(int x, int y) => new(x, y, 0);

    /// <summary>
    /// Open ground with a wall of blocked tiles six tiles out from the spawn on every side, and a few
    /// blocked tiles inside it.
    /// </summary>
    private static TileMap Walled()
    {
        var map = BotRun.OpenGround();
        for (var i = -6; i <= 6; i++)
        {
            foreach (var edge in new[] { -6, 6 })
            {
                map.SetFlags(At(32 + i, 32 + edge), TileFlags.Blocked);
                map.SetFlags(At(32 + edge, 32 + i), TileFlags.Blocked);
            }
        }

        foreach (var (x, y) in new[] { (36, 32), (34, 36), (29, 36) })
        {
            map.SetFlags(At(x, y), TileFlags.Blocked);
        }

        return map;
    }

    private async Task<BotRun> StartAsync(int bots, int seed = 1, int reach = 12, TileMap? map = null)
    {
        var run = await BotRun.StartAsync(bots, seed, reach, map);
        _runs.Add(run);
        return run;
    }

    private static void KillEveryCreature(BotRun run)
    {
        foreach (var npc in run.Server.Npcs.Standing.ToList())
        {
            run.Server.Npcs.Kill(npc);
        }
    }

    // Leaves bots nothing to fight or use, so the only thing they do is walk.
    private static void ClearGround(BotRun run)
    {
        foreach (var placed in new[] { 30, 31, 32, 33 }.Select(x => run.Server.Objects.At(At(x, 34))))
        {
            Assert.NotNull(placed);
            run.Server.Objects.Remove(placed);
        }

        KillEveryCreature(run);
    }

    [Fact]
    public async Task EveryBotLogsInAndEachTickIsTimed()
    {
        var run = await StartAsync(10);
        await run.StepAsync(20);

        var report = run.Report();
        Assert.Equal(10, report.Joined);
        Assert.Equal(20, report.TickTimes.Count);
        Assert.Empty(report.Problems);
        Assert.Equal(10, run.Probe.Players().Count);
    }

    [Fact]
    public async Task BotsWalkIntoWallsWithoutBreakingTheServer()
    {
        var map = Walled();
        var run = await StartAsync(20, map: map);
        await run.StepAsync(60);

        var report = run.Report();
        Assert.Equal(20, report.Joined);
        Assert.Empty(report.Problems);
        Assert.Equal(60, report.TickTimes.Count);
        var tiles = run.Probe.Players().Select(name => run.Probe.Player(name)?.Tile ?? default).ToList();
        Assert.Contains(tiles, tile => tile != At(32, 32));
        foreach (var tile in tiles)
        {
            Assert.InRange(Math.Abs(tile.X - 32), 0, 5);
            Assert.InRange(Math.Abs(tile.Y - 32), 0, 5);
            Assert.False(map.FlagsAt(tile).Has(TileFlags.Blocked));
        }
    }

    [Fact]
    public async Task BotsFightWhatTheyFind()
    {
        var run = await StartAsync(20);
        await run.StepAsync(80);

        var fought = run.Probe.Players().Where(name => run.Probe.Player(name)?.Experience.ContainsKey("fighting") == true);
        Assert.NotEmpty(fought);
    }

    [Fact]
    public async Task BotsWalkToTilesFarOutsideTheWorldWithoutBreaking()
    {
        var run = await StartAsync(10, reach: 1000);
        await run.StepAsync(30);

        Assert.Equal(10, run.Report().Joined);
        Assert.Empty(run.Report().Problems);
    }

    [Fact]
    public async Task BotsWithNothingToFightOrUseKeepGoing()
    {
        var run = await StartAsync(10);
        ClearGround(run);
        await run.StepAsync(30);

        Assert.Empty(run.Server.Npcs.Standing);
        Assert.Equal(10, run.Report().Joined);
        Assert.Empty(run.Report().Problems);
    }

    [Fact]
    public async Task ABotSeesTheWorldAsItIsATickOrTwoAgo()
    {
        var run = await StartAsync(1);
        await run.StepAsync(5);
        var session = Assert.Single(run.Bots).Session;
        Assert.NotEmpty(session.Npcs);

        KillEveryCreature(run);
        await run.StepAsync(10);

        Assert.Empty(session.Npcs);
    }

    [Fact]
    public async Task ABotTheServerDroppedIsNotCountedAsJoined()
    {
        var run = await StartAsync(3);
        await run.StepAsync(5);
        run.Server.Host.RemoveClient("bot-0001");
        await run.StepAsync(15);

        Assert.Equal(2, run.Report().Joined);
        Assert.False(run.Report().Healthy);
        Assert.Contains(run.Report().Problems, problem => problem.StartsWith("bot-0001 was sent away: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATickThatThrowsEndsTheRunAndIsRecorded()
    {
        var run = await StartAsync(2);
        await run.StepAsync(3);
        run.Server.Loop.Registry.AddSystem(Phase.Npcs, () => throw new InvalidOperationException("x"));
        await run.StepAsync(5);

        var report = run.Report();
        Assert.Equal(3, report.TickTimes.Count);
        Assert.Equal("the run stopped at tick 4: x", Assert.Single(report.Problems));
        Assert.False(report.Healthy);
    }

    [Fact]
    public async Task AReportKeepsTheTicksItWasTakenAt()
    {
        var run = await StartAsync(2);
        await run.StepAsync(3);
        var report = run.Report();

        await run.StepAsync(2);

        Assert.Equal(3, report.TickTimes.Count);
        Assert.Equal(5, run.Report().TickTimes.Count);
    }

    [Fact]
    public async Task TheSameSeedPlaysOutTheSameWay()
    {
        Assert.Equal(await PlayAsync(5), await PlayAsync(5));
    }

    [Fact]
    public async Task ADifferentSeedPlaysOutDifferently()
    {
        Assert.NotEqual(await PlayAsync(5), await PlayAsync(6));
    }

    [Fact]
    public async Task BotsWalkToBothSidesOfHomeAndNoFurtherThanTheirReach()
    {
        var run = await StartAsync(20, reach: 1);
        ClearGround(run);
        await run.StepAsync(60);

        var tiles = run.Probe.Players().Select(name => run.Probe.Player(name)?.Tile ?? default).ToList();
        // Players do not share a tile, so a walk to a crowded tile ends one tile beside it.
        Assert.All(tiles, tile => Assert.InRange(Math.Max(Math.Abs(tile.X - 32), Math.Abs(tile.Y - 32)), 0, 2));
        Assert.Contains(tiles, tile => tile.X == 31);
        Assert.Contains(tiles, tile => tile.X == 33);
        Assert.Contains(tiles, tile => tile.Y == 31);
        Assert.Contains(tiles, tile => tile.Y == 33);
    }

    [Fact]
    public async Task ABotDoesNothingUntilTheServerHasLetItIn()
    {
        var run = await StartAsync(20);
        await run.StepAsync(1);

        Assert.All(run.Bots, bot => Assert.True(bot.IsIn));
        Assert.All(run.Probe.Players(), name => Assert.False(run.Probe.Player(name)?.Moving));
        Assert.All(run.Probe.Players(), name => Assert.Equal(At(32, 32), run.Probe.Player(name)?.Tile));
    }

    [Fact]
    public async Task BotsUseTheFirstOptionOfAnObject()
    {
        var run = await StartAsync(5, reach: 0);
        ClearGround(run);
        var door = run.Server.Objects.Place(ObjectId.Door, At(33, 33));
        var opened = false;
        for (var i = 0; i < 80 && !opened; i++)
        {
            await run.StepAsync(1);
            opened = door.State != 0;
        }

        Assert.True(opened);
    }

    [Fact]
    public async Task TheReportNamesTheSeedTheRunWasStartedWith()
    {
        var run = await StartAsync(2, seed: 9);

        Assert.Equal(9, run.Report().Seed);
    }

    [Fact]
    public async Task ABotWithNoReachStaysAtHome()
    {
        var run = await StartAsync(5, reach: 0);
        ClearGround(run);
        await run.StepAsync(30);

        Assert.All(run.Probe.Players(), name => Assert.Equal(At(32, 32), run.Probe.Player(name)?.Tile));
    }

    private async Task<List<TileCoord>> PlayAsync(int seed)
    {
        var run = await StartAsync(8, seed: seed);
        await run.StepAsync(40);
        return [.. run.Probe.Players().Select(name => run.Probe.Player(name)?.Tile ?? default)];
    }
}
