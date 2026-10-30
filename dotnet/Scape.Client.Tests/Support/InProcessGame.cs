using Orblit.Net;
using Scape.Content;
using Scape.Core;
using Scape.Server;

namespace Scape.Client.Tests;

/// <summary>Never waits, so the test steps the loop by hand.</summary>
internal sealed class NoPacer : IPacer
{
    public Task UntilNextAsync() => Task.CompletedTask;
}

/// <summary>
/// A real server and any number of sessions on loopback links, with the client's clock in the
/// test's hands.
/// </summary>
internal sealed class InProcessGame : IAsyncDisposable
{
    public const double TickSeconds = 0.6;

    private readonly List<LoopbackLink> _links = [];
    private readonly List<GameSession> _sessions = [];

    public InProcessGame()
    {
        Server = new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(GameTick.Period, () => TimeSpan.Zero),
            Pacer = new NoPacer(),
            Log = _ => { },
            Map = OpenWorld(),
            Spawn = new TileCoord(32, 32, 0),
            Skills = DefaultSkills.All,
            Items = DefaultItems.All,
            Objects = DefaultObjects.All,
            Nodes = DefaultNodes.All,
            Npcs = DefaultNpcs.All,
            Story = DefaultStory.Story,
            Combat = DefaultCombat.Rules,
            Shops = DefaultShops.Rules,
            Spells = DefaultSpells.All,
            Prayers = DefaultPrayers.All,
            Random = new Random(7),
        });
    }

    public ScapeServer Server { get; }

    /// <summary>The network id of the player's own avatar. Fails when the server has not said.</summary>
    public static long AvatarOf(GameSession session) =>
        session.AvatarId ?? throw new InvalidOperationException("The server has not named an avatar.");

    /// <summary>Seconds on every session's clock.</summary>
    public double Seconds { get; set; }

    public GameSession Connect()
    {
        var link = new LoopbackLink();
        _links.Add(link);
        Server.Host.Accept(link.Host);
        var session = new GameSession(link.Client, new GameSessionOptions
        {
            Skills = DefaultSkills.All,
            Items = DefaultItems.All,
            Story = DefaultStory.Story,
            Clock = () => TimeSpan.FromSeconds(Seconds),
        });
        _sessions.Add(session);
        return session;
    }

    /// <summary>Connects and logs in as <paramref name="name"/>, then runs a tick so the player is placed.</summary>
    public async Task<GameSession> JoinAsync(string name)
    {
        var session = Connect();
        session.Login(name);
        await PumpAsync();
        Seconds += TickSeconds;
        await TickAsync();
        return session;
    }

    /// <summary>
    /// Lets the links deliver what is queued, and what that causes, then has each session take it in.
    /// A message to one session can make the host send to another, so it goes round twice.
    /// </summary>
    public async Task PumpAsync()
    {
        for (var round = 0; round < 2; round++)
        {
            foreach (var link in _links)
            {
                await link.IdleAsync();
            }
        }

        foreach (var session in _sessions)
        {
            session.Advance();
        }
    }

    /// <summary>Runs one server tick and delivers what it sent.</summary>
    public async Task TickAsync()
    {
        Server.Loop.RunTick();
        await PumpAsync();
    }

    /// <summary>
    /// Runs <paramref name="times"/> ticks, each a tick of the clock apart, and draws after each.
    /// Drawing runs behind the newest state by the interpolation delay, so the clock passes it first.
    /// </summary>
    public async Task TickAndDrawAsync(GameSession session, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Seconds += TickSeconds;
            await TickAsync();
            Seconds += GameSession.InterpolationDelay.TotalSeconds;
            session.Advance();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        foreach (var link in _links)
        {
            await link.CloseAsync();
            // A close is delivered like a message, so the host has not heard of it until the link is idle.
            await link.IdleAsync();
        }

        await Server.DisposeAsync();
    }

    private static TileMap OpenWorld()
    {
        var map = new TileMap();
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        map.AddRegion(FlagRegion.Open(new RegionKey(0, 0, 1)));
        return map;
    }
}
