using System.Diagnostics;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class NpcsTests(ITestOutputHelper output) : IAsyncDisposable
{
    private TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static int Gap(TileCoord a, TileCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static TileCoord WhereIs(TestClient client) => client.TileOf(client.Avatar)!.Value;

    /// <summary>A hound that sees <paramref name="sight"/> tiles and goes <paramref name="leash"/> from home.</summary>
    private static NpcSet Hound(int sight, int leash) => new([new NpcDef(1, "Hound", new Aggressive(sight, leash))]);

    /// <summary>Starts the server again with other creatures or other dice.</summary>
    private async Task StartAsync(NpcSet? npcs = null, Random? random = null)
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Npcs = npcs ?? options.Npcs, Random = random ?? options.Random });
    }

    private PlacedNpc Place(int npc, TileCoord tile, int radius = 0) => Server.Npcs.Spawn(new NpcSpawn(npc, tile, radius));

    /// <summary>Ticks <paramref name="times"/> times and calls <paramref name="check"/> after each.</summary>
    private async Task TickCheckingAsync(int times, Action check)
    {
        for (var i = 0; i < times; i++)
        {
            await _test.TickAsync();
            check();
        }
    }

    // Spawning.

    [Fact]
    public async Task ACreatureIsToldToAPlayerNearIt()
    {
        var ana = await _test.JoinAsync("Ana");
        var guard = Place(NpcId.Guard, At(36, 32));

        await _test.TickAsync(2);

        Assert.Equal(new Dictionary<long, int> { [(long)guard.NetworkId] = NpcId.Guard }, ana.Npcs);
        Assert.Equal(At(36, 32), ana.TileOf((long)guard.NetworkId));
    }

    [Fact]
    public void RefusesACreatureThatDoesNotExist() =>
        Assert.Throws<ArgumentException>(() => Server.Npcs.Spawn(new NpcSpawn(99, At(36, 32))));

    [Fact]
    public void RefusesGroundACreatureMayNotStandOn()
    {
        _test.Map.SetFlags(At(36, 32), TileFlags.Blocked);
        _test.Map.SetFlags(At(37, 32), TileFlags.Water);
        _test.Map.SetFlags(At(38, 32), TileFlags.NoNpc);

        foreach (var x in new[] { 36, 37, 38 })
        {
            Assert.Throws<ArgumentException>(() => Place(NpcId.Guard, At(x, 32)));
        }

        Assert.Equal(At(39, 32), Place(NpcId.Guard, At(39, 32)).Tile);
    }

    // A guard.

    [Fact]
    public async Task AGuardStaysWhereItIsPutWhateverItsRadius()
    {
        await _test.JoinAsync("Ana");
        var guard = Place(NpcId.Guard, At(36, 32), radius: 5);

        await TickCheckingAsync(80, () => Assert.Equal(At(36, 32), guard.Tile));
    }

    // A chicken.

    [Fact]
    public async Task AChickenWandersAndStaysInsideItsRadius()
    {
        await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 4);
        var seen = new HashSet<TileCoord>();

        await TickCheckingAsync(600, () =>
        {
            seen.Add(chicken.Tile);
            Assert.InRange(Gap(chicken.Tile, chicken.Home), 0, 4);
        });

        var home = chicken.Home;
        Assert.True(seen.Count > 5);
        Assert.Contains(seen, tile => Gap(tile, home) == 4);
        Assert.Contains(seen, tile => tile.X < home.X);
        Assert.Contains(seen, tile => tile.X > home.X);
        Assert.Contains(seen, tile => tile.Y < home.Y);
        Assert.Contains(seen, tile => tile.Y > home.Y);
    }

    [Fact]
    public async Task AChickenWandersAlongTheEdgeOfTheWorld()
    {
        var ana = await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(0, 0), radius: 4);
        await ana.SendAsync(new Walk(At(6, 6)));
        await _test.TickAsync(40);
        var seen = new HashSet<TileCoord>();

        await TickCheckingAsync(300, () => seen.Add(chicken.Tile));

        Assert.True(seen.Count > 3);
    }

    [Fact]
    public async Task AChickenStaysHomeWhenTheDiceAreOverTheChance()
    {
        await StartAsync(random: new Rolls(0.99));
        await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 4);

        await _test.TickAsync(50);

        Assert.Equal(chicken.Home, chicken.Tile);
    }

    [Fact]
    public async Task AChickenSetsOffWhenTheDiceAreUnderTheChance()
    {
        await StartAsync(random: new Rolls(0.0));
        await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 4);

        await _test.TickAsync(5);

        Assert.NotEqual(chicken.Home, chicken.Tile);
    }

    [Fact]
    public async Task AChickenIsShownWhereItStandsAsItWalks()
    {
        var ana = await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(36, 32), radius: 4);

        await TickCheckingAsync(100, () => Assert.Equal(chicken.Tile, ana.TileOf((long)chicken.NetworkId)));
    }

    [Fact]
    public async Task AChickenKeepsOutOfTilesFlaggedAgainstCreatures()
    {
        for (var y = 0; y < 128; y++)
        {
            for (var x = 41; x <= 43; x++)
            {
                _test.Map.SetFlags(At(x, y), TileFlags.NoNpc);
            }
        }

        await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 6);

        await TickCheckingAsync(600, () => Assert.True(chicken.Tile.X < 41));
    }

    // A goblin.

    [Fact]
    public async Task AGoblinFollowsAPlayerInSightAndStopsBesideThem()
    {
        var ana = await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(36, 32));

        await _test.TickAsync(10);

        var player = WhereIs(ana);
        Assert.Equal(1, Math.Abs(goblin.Tile.X - player.X) + Math.Abs(goblin.Tile.Y - player.Y));

        var stood = goblin.Tile;
        await _test.TickAsync(10);
        Assert.Equal(stood, goblin.Tile);
    }

    [Fact]
    public async Task AGoblinIgnoresAPlayerOutOfSight()
    {
        await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(45, 32));

        await _test.TickAsync(30);

        Assert.Equal(At(45, 32), goblin.Tile);
    }

    [Fact]
    public async Task AHoundGivesUpOnAPlayerPastItsLeash()
    {
        await StartAsync(Hound(sight: 4, leash: 5), new Rolls(0.99));
        var ana = await _test.JoinAsync("Ana");
        var dog = Place(1, At(40, 32), radius: 5);

        await ana.SendAsync(new Walk(At(45, 32)));
        await _test.TickAsync(15);
        Assert.Equal(At(44, 32), dog.Tile);

        await ana.SendAsync(new Walk(At(46, 32)));
        await _test.TickAsync(15);
        Assert.Equal(dog.Home, dog.Tile);
    }

    [Fact]
    public async Task AGoblinGoesForTheNearerOfTwoPlayers()
    {
        var ana = await _test.JoinAsync("Ana");
        var ben = await _test.JoinAsync("Ben");
        await ben.SendAsync(new Walk(At(32, 38)));
        await _test.TickAsync(10);
        Assert.Equal(At(32, 32), WhereIs(ana));
        Assert.Equal(At(32, 38), WhereIs(ben));

        var goblin = Place(NpcId.Goblin, At(36, 33));
        await _test.TickAsync();

        Assert.True(goblin.Tile.Y <= 33);
    }

    [Fact]
    public async Task AGoblinTurnsRoundToFollowAPlayerWhoWalksPast()
    {
        var ana = await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(37, 32));
        await ana.SendAsync(new Walk(At(44, 32)));
        await _test.TickAsync(8);

        await TickCheckingAsync(8, () => Assert.InRange(Gap(goblin.Tile, WhereIs(ana)), 0, 2));
    }

    [Fact]
    public async Task AGoblinGoesRoundAWallToAPlayerAndBackHome()
    {
        await StartAsync(random: new Rolls(0.99));
        for (var y = 0; y < 128; y++)
        {
            if (y != 25)
            {
                _test.Map.SetFlags(At(42, y), TileFlags.NoNpc);
            }
        }

        var ana = await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(40, 32));

        await ana.SendAsync(new Walk(At(44, 32)));
        await _test.TickAsync(40);
        Assert.Equal(1, Gap(goblin.Tile, WhereIs(ana)));

        await ana.SendAsync(new Walk(At(53, 32)));
        await _test.TickAsync(60);
        Assert.Equal(goblin.Home, goblin.Tile);
    }

    [Fact]
    public async Task AGoblinGoesHomeWhenThePlayerLeavesItsLeash()
    {
        await StartAsync(random: new Rolls(0.99));
        var ana = await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(40, 32), radius: 3);
        await ana.SendAsync(new Walk(At(38, 32)));
        await _test.TickAsync(4);
        Assert.NotEqual(goblin.Home, goblin.Tile);

        await ana.SendAsync(new Walk(At(32, 60)));
        await _test.TickAsync(30);

        Assert.Equal(goblin.Home, goblin.Tile);
    }

    [Fact]
    public async Task AGoblinNeverPassesItsLeashWhileAPlayerPullsAtIt()
    {
        var ana = await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(40, 32), radius: 3);

        for (var round = 0; round < 60; round++)
        {
            await ana.SendAsync(new Walk(At(round % 2 == 0 ? 52 : 53, 32)));
            await TickCheckingAsync(4, () => Assert.InRange(Gap(goblin.Tile, goblin.Home), 0, 12));
        }
    }

    [Fact]
    public async Task AHoundCutsADetourShortWhenItWouldPassTheLeash()
    {
        await StartAsync(Hound(sight: 8, leash: 8));
        for (var y = 20; y < 90; y++)
        {
            _test.Map.SetFlags(At(44, y), TileFlags.Blocked);
        }

        var ana = await _test.JoinAsync("Ana");
        var hound = Place(1, At(40, 32));
        await ana.SendAsync(new Walk(At(48, 32)));

        await TickCheckingAsync(100, () =>
        {
            Assert.InRange(Gap(hound.Tile, hound.Home), 0, 8);
            Assert.True(hound.Tile.X < 44);
        });
    }

    [Fact]
    public async Task AGoblinDoesNotFollowAPlayerIntoGroundFlaggedAgainstCreatures()
    {
        for (var y = 28; y <= 36; y++)
        {
            for (var x = 30; x <= 35; x++)
            {
                _test.Map.SetFlags(At(x, y), TileFlags.NoNpc);
            }
        }

        await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, At(38, 32));

        await TickCheckingAsync(30, () => Assert.True(goblin.Tile.X > 35));
    }

    [Fact]
    public async Task AGoblinIsNotDrawnToAPlayerOnAnotherPlane()
    {
        _test.Map.AddRegion(FlagRegion.Open(new RegionKey(0, 0, 1)));
        await _test.JoinAsync("Ana");
        var goblin = Place(NpcId.Goblin, new TileCoord(36, 32, 1));

        await _test.TickAsync(20);

        Assert.Equal(new TileCoord(36, 32, 1), goblin.Tile);
    }

    // A region nobody is near.

    [Fact]
    public async Task ARegionNobodyIsNearDoesNotTickItsCreatures()
    {
        var rolls = new Rolls(0.0);
        await StartAsync(random: rolls);
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 5);

        await _test.TickAsync(50);

        Assert.Equal(0, rolls.Drawn);
        Assert.Equal(chicken.Home, chicken.Tile);
    }

    [Fact]
    public async Task ACreatureIsPutBackAtHomeWhenItsRegionWakes()
    {
        var ana = await _test.JoinAsync("Ana");
        var chicken = Place(NpcId.Chicken, At(40, 32), radius: 6);
        await _test.TickAsync(200);
        Assert.NotEqual(chicken.Home, chicken.Tile);

        await ana.SendAsync(new Walk(At(100, 32)));
        while (Server.Active.IsActiveAt(40, 32))
        {
            await _test.TickAsync();
        }

        var stood = chicken.Tile;
        await _test.TickAsync(20);
        Assert.Equal(stood, chicken.Tile);

        await ana.SendAsync(new Walk(At(36, 32)));
        while (!Server.Active.IsActiveAt(40, 32))
        {
            await _test.TickAsync();
        }

        Assert.Equal(chicken.Home, chicken.Tile);
    }

    [Fact]
    public async Task ATickWithAThousandCreaturesStaysInsideItsBudget()
    {
        await _test.JoinAsync("Ana");
        var random = new Random(1);
        for (var i = 0; i < 1000; i++)
        {
            Place(i % 10 == 0 ? NpcId.Goblin : NpcId.Chicken, At(10 + random.Next(90), 10 + random.Next(90)), radius: 4);
        }

        await _test.TickAsync(5);

        var watch = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        for (var i = 0; i < 30; i++)
        {
            var before = watch.Elapsed;
            Server.Loop.RunTick();
            slowest = TimeSpan.FromTicks(Math.Max(slowest.Ticks, (watch.Elapsed - before).Ticks));
            await _test.PumpAsync();
        }

        var average = watch.Elapsed / 30;

        // The figure is recorded in the task file.
        output.WriteLine($"1000 creatures: average {average.TotalMilliseconds:F2} ms a tick, slowest {slowest.TotalMilliseconds:F2} ms");
        Assert.Equal(1000, Server.Npcs.Count);
        Assert.True(average < GameTick.Period / 2);
    }
}
