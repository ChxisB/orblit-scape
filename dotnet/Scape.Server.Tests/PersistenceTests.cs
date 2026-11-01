using System.Collections.Concurrent;
using Orblit.Net;
using Orblit.Save;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class PersistenceTests : IAsyncDisposable
{
    private readonly List<TestServer> _servers = [];
    private readonly ConcurrentQueue<string> _logged = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var test in _servers)
        {
            await test.DisposeAsync();
        }
    }

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static SaveRecord Damaged() => new([1, 2, 3], new Autosave(), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static Features FeaturesOf(ScapeServer server) => new(
        server.Movement,
        server.Social,
        server.Items,
        server.Ground,
        server.Npcs,
        server.Actors,
        server.Objects,
        server.Skills,
        server.Interactions,
        server.Combat,
        server.Prayers)
    {
        Quests = server.Quests,
    };

    private TestServer Start(ISaveStore store) => Start(Stores.On(store));

    private TestServer Start(Storage storage)
    {
        var test = new TestServer(options => options with { Log = _logged.Enqueue, Storage = storage });
        _servers.Add(test);
        return test;
    }

    // A restart.

    [Fact]
    public async Task ARestartKeepsSkillsItemsTileAndLists()
    {
        var store = new MemorySaveStore();
        var first = Start(store);
        var ana = await first.JoinAsync("Ana");
        first.Server.Skills.Grant("ana", "woodcutting", 500);
        first.Server.Items.ContainerOf("ana", ContainerId.Inventory)!.Add(ItemId.Logs, 3);
        first.Server.Social.Restore("ana", ["bo"], ["cy"]);
        await ana.SendAsync(new Walk(At(36, 32)));
        await first.TickAsync(4);
        await first.DisposeAsync();

        var second = Start(store);
        await second.JoinAsync("Ana");

        var server = second.Server;
        Assert.Equal(500, server.Skills.XpOf("ana", "woodcutting"));
        Assert.Equal(3, server.Items.ContainerOf("ana", ContainerId.Inventory)!.CountOf(ItemId.Logs));
        Assert.Equal(["bo"], server.Social.ListsOf("ana").Friends);
        Assert.Equal(["cy"], server.Social.ListsOf("ana").Ignored);
        Assert.Equal(At(36, 32), server.Movement.TileOf("ana"));
    }

    [Fact]
    public async Task ARestartGivesTheHealthTheSavedSkillAllows()
    {
        var store = new MemorySaveStore();
        var first = Start(store);
        await first.JoinAsync("Ana");
        first.Server.Skills.Grant("ana", "health", 5000);
        await first.DisposeAsync();

        var second = Start(store);
        await second.JoinAsync("Ana");
        await second.JoinAsync("Bo");

        var combat = second.Server.Combat;
        Assert.True(combat.HealthOfPlayer("ana")!.Value.Max > combat.HealthOfPlayer("bo")!.Value.Max);
    }

    [Fact]
    public async Task ARestartLogsWhatASaveHasThatTheGameNoLongerHas()
    {
        var store = new MemorySaveStore();
        var bag = new ItemStack?[Container.SizeOf(ContainerId.Inventory)];
        bag[0] = new ItemStack(65000, 1);
        var state = new PlayerState(
            OpenWorld.SpawnTile,
            skills: new Dictionary<string, long> { ["juggling"] = 50 },
            containers: new Dictionary<ContainerId, IReadOnlyList<ItemStack?>> { [ContainerId.Inventory] = bag });
        await store.WriteAsync(StoreKeys.SaveOf("ana"), new SaveRecord(state.Encode(), new Autosave(), DateTimeOffset.UnixEpoch));
        var test = Start(store);

        var ana = await test.JoinAsync("Ana");

        Assert.True(ana.Admitted);
        Assert.Contains("ana: 2 saved entries were left out", _logged);
    }

    [Fact]
    public async Task ARestartPutsAPlayerWhoseTileIsNowBlockedAtTheSpawn()
    {
        var store = new MemorySaveStore();
        var first = Start(store);
        var ana = await first.JoinAsync("Ana");
        await ana.SendAsync(new Walk(At(36, 32)));
        await first.TickAsync(4);
        await first.DisposeAsync();

        var second = Start(store);
        second.Map.SetFlags(At(36, 32), TileFlags.Blocked);
        await second.JoinAsync("Ana");

        Assert.Equal(OpenWorld.SpawnTile, second.Server.Movement.TileOf("ana"));
    }

    [Fact]
    public async Task ARestartRefusesADamagedSaveAndLeavesItAsItWas()
    {
        var store = new MemorySaveStore();
        await store.WriteAsync(StoreKeys.SaveOf("ana"), Damaged());
        var test = Start(store);

        var ana = await test.JoinAsync("Ana");
        await test.DisposeAsync();

        Assert.False(ana.Admitted);
        Assert.Equal([LoginRefusal.Unavailable.ToWire()], ana.Goodbyes);
        Assert.Equal([1, 2, 3], (await store.ReadAsync(StoreKeys.SaveOf("ana")))!.Bytes.ToArray());
    }

    // A login.

    [Fact]
    public async Task ALoginThatComesWhileTheOldSessionEndsGetsTheNewestSave()
    {
        var store = new GatedStore(new MemorySaveStore());
        var test = Start(store);
        var first = await test.JoinAsync("Ana");
        test.Server.Skills.Grant("ana", "mining", 400);
        await first.Link.CloseAsync();
        // Until the host hears of the close, Ana is live and the next login is told the name is taken.
        await first.Link.IdleAsync();

        var gate = store.ReadGate = Stores.Gate();
        var second = test.Connect();
        await second.LoginAsync("Ana");
        await test.TickAsync(12);
        Assert.Null(test.Server.Movement.TileOf("ana"));
        gate.SetResult();
        await test.UntilAsync(() => second.Admitted);
        await test.TickAsync();

        Assert.Equal(400, test.Server.Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task ALoginThatResumesASessionIsNotStoppedByADamagedSave()
    {
        var store = new MemorySaveStore();
        var test = Start(store);
        var first = await test.JoinAsync("Ana");
        await first.Link.CloseAsync();
        await test.TickAsync(2);
        await store.WriteAsync(StoreKeys.SaveOf("ana"), Damaged());

        var again = await test.JoinAsync("Ana");

        // A resume is not a join, so it gets no LoginOk.
        Assert.Empty(again.Goodbyes);
        Assert.Equal(SessionState.Live, test.Server.Host.SessionStateOf("ana"));
    }

    [Fact]
    public async Task ALoginThatCannotBeDecidedIsLoggedWithItsName()
    {
        var store = new MemorySaveStore();
        await store.WriteAsync(StoreKeys.SaveOf("ana"), Damaged());
        var test = Start(store);

        await test.JoinAsync("Ana");

        Assert.Contains("Login of ana failed", Assert.Single(_logged), StringComparison.Ordinal);
        Assert.Equal(1, test.Server.Wire.Unavailable);
    }

    [Fact]
    public async Task ALoginAfterATradeRecoveryThatFailedTriesItAgain()
    {
        var test = Start(new SlowStartStore(new MemorySaveStore()));

        var first = await test.JoinAsync("Ana");
        var second = await test.JoinAsync("Ana");

        Assert.Equal([LoginRefusal.Unavailable.ToWire()], first.Goodbyes);
        Assert.True(second.Admitted);
    }

    [Fact]
    public async Task ASecondLoginForANameStillBeingDecidedHearsBusy()
    {
        var store = new GatedStore(new MemorySaveStore());
        var test = Start(store);
        var gate = store.ReadGate = Stores.Gate();
        var first = test.Connect();
        var second = test.Connect();
        try
        {
            await first.LoginAsync("Ana");
            await second.LoginAsync("Ana");
            await test.UntilAsync(() => second.Goodbyes.Count > 0);
        }
        finally
        {
            // A failed wait must not leave the first login on the gate.
            gate.TrySetResult();
        }

        await test.UntilAsync(() => first.Admitted);
        Assert.Equal([LoginRefusal.Busy.ToWire()], second.Goodbyes);
    }

    [Fact]
    public async Task ALoginThatResumesALingeringSessionKeepsWhatTheWorldHas()
    {
        var store = new MemorySaveStore();
        var test = Start(store);
        var first = await test.JoinAsync("Ana");
        test.Server.Skills.Grant("ana", "mining", 900);
        await first.Link.CloseAsync();
        await test.TickAsync(2);

        await test.JoinAsync("Ana");

        Assert.Equal(900, test.Server.Skills.XpOf("ana", "mining"));
    }

    /// <summary>
    /// The host decides a login on its own thread, so a session the login found held can end
    /// before the host seats the player. This plays the host's part by hand, in its order.
    /// </summary>
    [Fact]
    public async Task ALoginThatFoundItsSessionHeldJoinsWithWhatTheWorldHadWhenTheSessionEndsFirst()
    {
        var test = new TestServer();
        _servers.Add(test);
        SessionState? session = SessionState.Lingering;
        var saves = new Saves(new SavesOptions
        {
            Queue = Stores.On(new MemorySaveStore()).Queue,
            States = new PlayerStates(FeaturesOf(test.Server), test.Map),
            AutoSaver = new AutoSaver(perTick: 1, every: 1000, jitter: 0, new Random(1)),
            SessionOf = _ => session,
            Log = _logged.Enqueue,
        });
        var ana = await test.JoinAsync("Ana");
        test.Server.Skills.Grant("ana", "mining", 400);

        await saves.PrepareAsync("ana");
        saves.Left("ana", GoneReason.Disconnect);
        await test.LeaveAsync(ana);
        await test.TickAsync(11);
        session = null;
        await test.JoinAsync("Ana");
        saves.Arrived("ana");

        Assert.Equal(400, test.Server.Skills.XpOf("ana", "mining"));
    }

    // A save.

    [Fact]
    public async Task ATickOf100SavesDoesNotWaitOnTheStore()
    {
        var store = new GatedStore(new MemorySaveStore());
        var storage = Stores.On(store) with { AutoSaver = new AutoSaver(perTick: 100, every: 1, jitter: 0, new Random(1)) };
        var test = Start(storage);
        for (var i = 0; i < 100; i++)
        {
            await test.JoinAsync($"P{i}");
        }

        var gate = store.WriteGate = Stores.Gate();
        try
        {
            var before = storage.Queue.Pending;

            await test.TickAsync();

            Assert.Equal(100, storage.Queue.Pending - before);
        }
        finally
        {
            // A failed assert must not leave the stop waiting on the gate.
            gate.TrySetResult();
        }

        await storage.Queue.FlushAsync();
        Assert.Equal(0, storage.Queue.Pending);
        Assert.Equal(100, (await store.KeysAsync()).Count);
    }

    [Fact]
    public async Task ASaveIsWrittenForAPlayerWhoIsStillInTheWorldOnStop()
    {
        var store = new GatedStore(new MemorySaveStore());
        var test = Start(store);
        await test.JoinAsync("Ana");
        test.Server.Skills.Grant("ana", "fishing", 250);
        var gate = store.WriteGate = Stores.Gate();

        var stopping = test.DisposeAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(await store.Inner.ExistsAsync(StoreKeys.SaveOf("ana")));
        gate.SetResult();
        await stopping;

        var saved = PlayerState.Decode((await store.ReadAsync(StoreKeys.SaveOf("ana")))!.Bytes.Span);
        Assert.Equal(250, saved.Skills["fishing"]);
    }

    [Fact]
    public async Task ARemovedPlayerIsSavedAsALeaveAndADroppedOneAsADisconnect()
    {
        var store = new MemorySaveStore();
        var storage = Stores.On(store);
        var test = Start(storage);
        await test.JoinAsync("Ana");
        var bo = await test.JoinAsync("Bo");

        test.Server.Host.RemoveClient("ana");
        await bo.Link.CloseAsync();
        await test.TickAsync(12);
        await storage.Queue.FlushAsync();

        Assert.IsType<Leave>((await store.ReadAsync(StoreKeys.SaveOf("ana")))!.Reason);
        Assert.IsType<Disconnect>((await store.ReadAsync(StoreKeys.SaveOf("bo")))!.Reason);
    }

    [Fact]
    public async Task AStopLogsTheSavesItCouldNotWrite()
    {
        var store = new SpyStore(new MemorySaveStore());
        var test = Start(store);
        await test.JoinAsync("Ana");
        store.Failing.Add(StoreKeys.SaveOf("ana"));

        await test.DisposeAsync();

        Assert.Contains($"Not written: {StoreKeys.SaveOf("ana")}", _logged);
    }

    [Fact]
    public async Task ASaveCanBeRolledBackAndKeepsWhatItReplaced()
    {
        var dir = Directory.CreateTempSubdirectory("scape_saves");
        try
        {
            var store = HistoryStore.Files(dir.FullName);
            var clock = Stores.Stepping();
            var first = Start(Stores.On(store, clock));
            await first.JoinAsync("Ana");
            first.Server.Skills.Grant("ana", "cooking", 100);
            await first.DisposeAsync();
            var older = Assert.Single(await store.History.ListAsync(StoreKeys.SaveOf("ana")));
            var second = Start(Stores.On(store, clock));
            await second.JoinAsync("Ana");
            second.Server.Skills.Grant("ana", "cooking", 900);
            await second.DisposeAsync();

            await new SaveTools(store, _ => false, clock).RestoreAsync(StoreKeys.SaveOf("ana"), older.At);
            var reasons = (await store.History.ListAsync(StoreKeys.SaveOf("ana"))).Select(entry => entry.Reason.ToString());
            var third = Start(Stores.On(store, clock));
            await third.JoinAsync("Ana");

            Assert.Equal(100, third.Server.Skills.XpOf("ana", "cooking"));
            Assert.Contains(reasons, reason => reason.Contains("pre-restore", StringComparison.Ordinal));
        }
        finally
        {
            await DisposeAsync();
            dir.Delete(recursive: true);
        }
    }
}
