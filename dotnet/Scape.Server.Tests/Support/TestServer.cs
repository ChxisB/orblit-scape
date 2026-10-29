using Orblit.Net;
using Scape.Core;

namespace Scape.Server.Tests;

/// <summary>A server on an open world, its clock and the clients connected to it.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private readonly List<TestClient> _clients = [];

    public TestServer()
    {
        Server = new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(GameTick.Period, Time.Read),
            Pacer = new NoPacer(),
            Log = _ => { },
            Map = Map,
            Spawn = OpenWorld.SpawnTile,
        });
    }

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
