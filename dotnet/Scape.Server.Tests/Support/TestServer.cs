using Orblit.Net;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

/// <summary>A server on an open world, its clock and the clients connected to it.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly List<TestClient> _clients = [];

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

    /// <summary>Connects a client, logs it in as <paramref name="name"/> and runs a tick so it is placed.</summary>
    public async Task<TestClient> JoinAsync(string name)
    {
        var client = Connect();
        await client.LoginAsync(name);
        await TickAsync();
        return client;
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

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        Server.Dispose();
    }
}
