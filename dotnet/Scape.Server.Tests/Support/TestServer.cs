using System.Diagnostics;
using Orblit.Net;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

/// <summary>A server on an open world, its clock and the clients connected to it.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly List<TestClient> _clients = [];
    private bool _disposed;

    /// <param name="change">Changes the default <see cref="Options"/>, such as to other creatures or dice.</param>
    public TestServer(Func<ScapeServerOptions, ScapeServerOptions>? change = null)
    {
        var options = Options(Map, new TickClock(GameTick.Period, Time.Read));
        Server = new ScapeServer(change is null ? options : change(options));
    }

    /// <summary>
    /// A server on <paramref name="map"/> at <see cref="OpenWorld.SpawnTile"/> with the default
    /// content, no pacing, no log and dice seeded with 7. Recipes are left out, as the server's
    /// own default leaves them.
    /// </summary>
    public static ScapeServerOptions Options(TileMap map, TickClock clock) => new()
    {
        Clock = clock,
        Pacer = new NoPacer(),
        Log = _ => { },
        Map = map,
        Spawn = OpenWorld.SpawnTile,
        Items = DefaultItems.All,
        Objects = DefaultObjects.All,
        Npcs = DefaultNpcs.All,
        Skills = DefaultSkills.All,
        Nodes = DefaultNodes.All,
        Shops = DefaultShops.Rules,
        Story = DefaultStory.Story,
        Combat = DefaultCombat.Rules,
        Random = new Random(7),
    };

    public FakeTime Time { get; } = new();

    /// <summary>The server's map. A test may block tiles on it.</summary>
    public TileMap Map { get; } = OpenWorld.Map();

    public ScapeServer Server { get; }

    public TestClient Connect()
    {
        var client = new TestClient(Server);
        _clients.Add(client);
        return client;
    }

    /// <summary>
    /// Connects a client, logs it in as <paramref name="name"/>, waits for the server to decide and
    /// runs a tick so it is placed. A server with storage decides off the tick.
    /// </summary>
    public async Task<TestClient> JoinAsync(string name, string password = "", bool create = false)
    {
        var client = Connect();
        await client.LoginAsync(name, password, create);
        var key = Wire.NameKey(name);
        await UntilAsync(() => client.Admitted || client.Goodbyes.Count > 0 || Server.Host.SessionStateOf(key) == SessionState.Live);
        await TickAsync();
        return client;
    }

    /// <summary>Lets the links deliver until <paramref name="done"/> holds. Fails after five seconds.</summary>
    public async Task UntilAsync(Func<bool> done)
    {
        var waited = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(5), "Timed out");
            await Task.Delay(1);
            await PumpAsync();
        }
    }

    /// <summary>Closes a client's link and stops pumping it, as when a player quits.</summary>
    public async Task LeaveAsync(TestClient client)
    {
        _clients.Remove(client);
        await client.DisposeAsync();
    }

    /// <summary>
    /// Lets every link deliver what is queued, and what that causes. A message to one client can
    /// make the host send to another, so it goes round twice.
    /// </summary>
    public async Task PumpAsync()
    {
        for (var round = 0; round < 2; round++)
        {
            foreach (var client in _clients)
            {
                await client.Link.IdleAsync();
            }
        }
    }

    /// <summary>Runs <paramref name="times"/> ticks, letting the links deliver after each.</summary>
    public async Task TickAsync(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Server.Loop.RunTick();
            await PumpAsync();
        }
    }

    /// <summary>Closes every client, then stops the server, which saves. A second call does nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        await Server.DisposeAsync();
    }
}
