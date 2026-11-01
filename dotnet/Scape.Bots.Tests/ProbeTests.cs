using Orblit.Net;
using Scape.Content;
using Scape.Core;
using Scape.Server;

namespace Scape.Bots.Tests;

public sealed class ProbeTests : IAsyncDisposable
{
    private BotRun? _run;

    public ValueTask DisposeAsync() => _run?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static TileCoord At(int x, int y) => new(x, y, 0);

    // The bots never act here, so they stand where they spawned.
    private async Task<(BotRun Run, ServerProbe Probe)> StartedAsync()
    {
        _run = await BotRun.StartAsync(3);
        await _run.Probe.StepAsync(3);
        return (_run, _run.Probe);
    }

    [Fact]
    public async Task ListsThePlayersInTheWorld()
    {
        var (_, probe) = await StartedAsync();

        Assert.Equal(["bot-0000", "bot-0001", "bot-0002"], probe.Players());
    }

    [Fact]
    public async Task ReadsAPlayerAndNothingForANameThatIsNotInTheWorld()
    {
        var (_, probe) = await StartedAsync();

        var view = probe.Player("bot-0001");

        Assert.NotNull(view);
        Assert.Equal(At(32, 32), view.Tile);
        Assert.False(view.Moving);
        Assert.False(view.Fighting);
        Assert.Equal(view.Health?.Max, view.Health?.Now);
        Assert.Null(probe.Player("nobody"));
    }

    [Fact]
    public async Task ObservesThePlayersCreaturesAndObjectsInAnArea()
    {
        var (_, probe) = await StartedAsync();

        var area = probe.Area(At(32, 32), 6);

        Assert.Equal(3, area.Players.Count);
        Assert.Equal(4, area.Creatures.Count);
        Assert.All(area.Creatures, creature => Assert.Equal(NpcId.Chicken, creature.Def));
        Assert.Equal(4, area.Objects.Count);
        Assert.All(area.Objects, placed => Assert.Equal(ObjectId.Tree, placed.Def));
    }

    [Fact]
    public async Task LeavesOutWhatIsOutsideTheArea()
    {
        var (_, probe) = await StartedAsync();

        Assert.Empty(probe.Area(At(32, 32), 1).Objects);
        Assert.Equal(4, probe.Area(At(32, 32), 2).Objects.Count);
        var upstairs = probe.Area(new TileCoord(32, 32, 1), 6);
        Assert.Empty(upstairs.Players);
        Assert.Empty(upstairs.Creatures);
        Assert.Empty(upstairs.Objects);
        var far = probe.Area(At(100, 100), 5);
        Assert.Empty(far.Players);
        Assert.Empty(far.Creatures);
        Assert.Empty(far.Objects);
    }

    [Fact]
    public async Task IncludesAPlayerExactlyAtTheRadiusOnEachAxis()
    {
        var (_, probe) = await StartedAsync();

        Assert.Equal(3, probe.Area(At(35, 32), 3).Players.Count);
        Assert.Empty(probe.Area(At(35, 32), 2).Players);
        Assert.Equal(3, probe.Area(At(32, 35), 3).Players.Count);
        Assert.Empty(probe.Area(At(32, 35), 2).Players);
    }

    [Fact]
    public async Task ObservesAnAreaAtTheEdgeOfTheWorld()
    {
        var (_, probe) = await StartedAsync();

        Assert.Empty(probe.Area(At(0, 0), 3).Players);
    }

    [Fact]
    public async Task ObservesAnAreaAtTheFarEdgeOfTheWorld()
    {
        var (_, probe) = await StartedAsync();
        var last = TileWorld.WorldSize - 1;

        Assert.Empty(probe.Area(At(last, last), 3).Players);
    }

    [Fact]
    public async Task ListsAnObjectThatCoversSeveralTilesOnce()
    {
        const int hall = 99;
        await using var server = new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(GameTick.Period),
            Pacer = new NoPacer(),
            Log = _ => { },
            Map = BotRun.OpenGround(),
            Spawn = At(32, 32),
            Skills = DefaultSkills.All,
            Items = DefaultItems.All,
            Objects = new ObjectSet([.. DefaultObjects.All.Objects, new ObjectDef(hall, "Hall", width: 2, height: 2)]),
            Nodes = DefaultNodes.All,
            Npcs = DefaultNpcs.All,
            Story = DefaultStory.Story,
            Shops = DefaultShops.Rules,
            Combat = DefaultCombat.Rules,
        });
        var placed = server.Objects.Place(hall, At(32, 32));

        var seen = Assert.Single(new ServerProbe(server, () => Task.CompletedTask).Area(At(32, 32), 3).Objects);

        Assert.Equal(placed.NetworkId, seen.NetworkId);
        Assert.Equal(At(32, 32), seen.Tile);
    }

    [Fact]
    public async Task RefusesAnAreaThatIsTooWideOrHasANegativeRadius()
    {
        var (_, probe) = await StartedAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => probe.Area(At(32, 32), ServerProbe.MaxAreaRadius + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => probe.Area(At(32, 32), -1));
    }

    [Fact]
    public async Task DeliversWhatAClientSentBeforeItRunsTheTick()
    {
        var (run, probe) = await StartedAsync();

        run.Bots[0].Session.WalkTo(At(36, 32));
        await probe.StepAsync(1);

        Assert.True(probe.Player("bot-0000")?.Moving);
    }

    [Fact]
    public async Task SamplesTheLengthOfEachTickItSteps()
    {
        var (_, probe) = await StartedAsync();
        var before = probe.TickTimes.Count;

        await probe.StepAsync(5);

        Assert.Equal(before + 5, probe.TickTimes.Count);
        Assert.All(probe.TickTimes, time => Assert.True(time >= TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => probe.StepAsync(-1));
    }

    private sealed class NoPacer : IPacer
    {
        public Task UntilNextAsync() => Task.CompletedTask;
    }
}
