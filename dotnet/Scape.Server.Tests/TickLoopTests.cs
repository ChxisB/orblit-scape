using Orblit.Net;

namespace Scape.Server.Tests;

public sealed class TickLoopTests : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(600);

    private readonly FakeTime _time = new();
    private readonly List<string> _lines = [];
    private ScapeServer? _server;

    public void Dispose() => _server?.Dispose();

    private ScapeServer Build(int ticks)
    {
        var pacer = new CountingPacer(ticks);
        _server = new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(Period, _time.Read),
            Pacer = pacer,
            Log = _lines.Add,
            Map = OpenWorld.Map(),
            Spawn = OpenWorld.SpawnTile,
        });
        pacer.Loop = _server.Loop;
        return _server;
    }

    [Fact]
    public async Task RunsThePhasesInOrderOnEveryOneOf100Ticks()
    {
        var server = Build(100);
        var seen = new List<Phase>();
        foreach (var phase in Enum.GetValues<Phase>())
        {
            server.Registry.AddSystem(phase, () => seen.Add(phase));
        }

        await server.Loop.RunAsync();

        Assert.Equal(100, server.Loop.Tick);
        Assert.Equal(100, server.Host.Tick);
        Assert.Equal(Enumerable.Repeat(Enum.GetValues<Phase>(), 100).SelectMany(phases => phases), seen);
    }

    [Fact]
    public async Task TimesEachPhaseThenTheHostTimesItsOwnStages()
    {
        var server = Build(5);
        await server.Loop.RunAsync();

        var names = server.Host.Summary().Phases.Keys.ToList();
        Assert.Equal(["inbound", "actions", "npcs", "movement", "combat", "outbound"], names.Take(6));
        Assert.Contains("capture", names);
    }

    [Fact]
    public async Task RunsDueActionsInTheActionsPhase()
    {
        var server = Build(3);
        var ran = new List<string>();
        var kind = new ActionKind("wave", delay: 1);
        server.Registry.AddKind(kind);
        server.Registry.AddSystem(Phase.Inbound, () =>
        {
            if (server.Loop.Tick == 1)
            {
                server.Registry.Actions.Submit(7, kind, () => ran.Add($"wave on tick {server.Loop.Tick}"));
            }
        });
        server.Registry.AddSystem(Phase.Actions, () => ran.Add("actions"));

        await server.Loop.RunAsync();

        Assert.Equal(["actions", "wave on tick 2", "actions", "actions"], ran);
    }

    [Fact]
    public async Task StartingWithNoFeaturesRegisteredRunsAndIdles()
    {
        var server = Build(20);

        await server.Loop.RunAsync();

        Assert.Equal(20, server.Loop.Tick);
        Assert.Equal(0, server.Host.ClientCount);
        Assert.Empty(_lines);
    }

    [Fact]
    public async Task ALateTickLogsTheSummaryOfWhereTheTimeWent()
    {
        var server = Build(3);
        server.Registry.AddSystem(Phase.Movement, () => _time.Advance(Period * 2));

        await server.Loop.RunAsync();

        var line = Assert.Single(_lines);
        Assert.Contains("late tick", line, StringComparison.Ordinal);
        Assert.Contains("movement", line, StringComparison.Ordinal);
        Assert.Contains("1200.0 ms", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopEndsTheLoopBeforeAnotherTickRuns()
    {
        var server = Build(1000);
        server.Registry.AddSystem(Phase.Npcs, server.Loop.Stop);

        await server.Loop.RunAsync();

        Assert.Equal(1, server.Loop.Tick);
    }

    /// <summary>Stops the loop after a number of ticks and never sleeps.</summary>
    private sealed class CountingPacer(int ticks) : IPacer
    {
        private int _waits;

        public TickLoop? Loop { get; set; }

        public Task UntilNextAsync()
        {
            _waits++;
            if (_waits > ticks)
            {
                Loop?.Stop();
            }

            return Task.CompletedTask;
        }
    }
}
