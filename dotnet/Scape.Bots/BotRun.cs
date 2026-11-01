using System.Collections.Concurrent;
using Orblit.Net;
using Scape.Client;
using Scape.Content;
using Scape.Core;
using Scape.Server;

namespace Scape.Bots;

/// <summary>
/// A server with bots on loopback links. The bots are the same client the app uses, so what they
/// send and read is what a player's client does. The server runs here, one tick at a time.
/// </summary>
public sealed class BotRun : IAsyncDisposable
{
    private static readonly TileCoord Spawn = new(32, 32, 0);

    private readonly List<Bot> _bots = [];
    private readonly List<LoopbackLink> _links = [];
    private readonly ConcurrentQueue<string> _problems = new();
    private readonly Random _random;
    private readonly int _reach;

    private BotRun(int seed, int reach, TileMap map)
    {
        Seed = seed;
        _reach = reach;
        _random = new Random(seed);
        Server = ServerOn(map, _problems.Enqueue, new Random(_random.Next(1 << 30)));
        Probe = new ServerProbe(Server, DeliverAsync);
    }

    public ScapeServer Server { get; }

    public ServerProbe Probe { get; }

    public IReadOnlyList<Bot> Bots => _bots;

    public int Seed { get; }

    /// <summary>Flat open ground, two regions across and two up, on plane 0.</summary>
    public static TileMap OpenGround()
    {
        var map = new TileMap();
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        return map;
    }

    /// <summary>
    /// Starts a server on <paramref name="map"/> with trees and chickens round the spawn tile, which
    /// must be open ground, and logs in <paramref name="bots"/> bots. A bot walks up to
    /// <paramref name="reach"/> tiles from the spawn. The <paramref name="seed"/> fixes the bots and
    /// the server's dice, so the same seed plays out the same way.
    /// </summary>
    public static async Task<BotRun> StartAsync(int bots, int seed = 1, int reach = 12, TileMap? map = null)
    {
        // shortcut: a join that throws leaves the server undisposed. The CLI exits on it, so upgrade when a long-lived caller starts runs.
        var run = new BotRun(seed, reach, map ?? OpenGround());
        for (var i = 0; i < bots; i++)
        {
            await run.JoinAsync($"bot-{i:D4}");
        }

        return run;
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks. Each bot acts, the server ticks, and then each bot reads
    /// what it was sent. A tick that throws ends the run early and is recorded as a problem, so the
    /// ticks measured so far are kept.
    /// </summary>
    public async Task StepAsync(int ticks)
    {
        try
        {
            for (var i = 0; i < ticks; i++)
            {
                await ActAsync();
                await Probe.StepAsync(1);
                foreach (var bot in _bots)
                {
                    bot.Session.Advance();
                }
            }
        }
#pragma warning disable CA1031 // A tick that throws is a finding for the report, not a crash.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _problems.Enqueue($"the run stopped at tick {Server.Loop.Tick}: {error.Message}");
        }
    }

    public BotReport Report() => new()
    {
        Machine = Machine.Current(),
        Bots = _bots.Count,
        Joined = _bots.Count(bot => Probe.Player(bot.Name) is not null),
        Seed = Seed,
        TickTimes = [.. Probe.TickTimes],
        Problems =
        [
            .. _problems,
            .. _bots.Where(bot => bot.Session.Refusal is not null).Select(bot => $"{bot.Name} was sent away: {bot.Session.Refusal}"),
        ],
    };

    public async ValueTask DisposeAsync()
    {
        foreach (var bot in _bots)
        {
            await bot.Session.DisposeAsync();
        }

        foreach (var link in _links)
        {
            await link.CloseAsync();
            // A close is delivered like a message, so the host has not heard of it until the link is idle.
            await link.IdleAsync();
        }

        await Server.DisposeAsync();
    }

    private static ScapeServer ServerOn(TileMap map, Action<string> log, Random random)
    {
        var server = new ScapeServer(new ScapeServerOptions
        {
            Clock = new TickClock(GameTick.Period),
            Pacer = new NoPacer(),
            Log = log,
            Map = map,
            Spawn = Spawn,
            Skills = DefaultSkills.All,
            Items = DefaultItems.All,
            Objects = DefaultObjects.All,
            Nodes = DefaultNodes.All,
            Npcs = DefaultNpcs.All,
            Story = DefaultStory.Story,
            Shops = DefaultShops.Rules,
            Combat = DefaultCombat.Rules,
            Random = random,
        });
        for (var i = 0; i < 4; i++)
        {
            server.Objects.Place(ObjectId.Tree, new TileCoord(30 + i, 34, 0));
            server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, new TileCoord(30 + i, 30, 0), 2));
        }

        return server;
    }

    private async Task JoinAsync(string name)
    {
        var link = new LoopbackLink();
        _links.Add(link);
        Server.Host.Accept(link.Host);
        var session = new GameSession(link.Client, new GameSessionOptions
        {
            Skills = DefaultSkills.All,
            Items = DefaultItems.All,
            Story = DefaultStory.Story,

            // The server ticks far faster than its period here, so the client's time follows the tick
            // count or it would show a stale world.
            Clock = () => GameTick.Period * Server.Loop.Tick,
        });
        _bots.Add(new Bot(new BotOptions
        {
            Name = name,
            Session = session,
            Home = Spawn,
            Reach = _reach,
            Random = new Random(_random.Next(1 << 30)),
        }));

        // Logins reach the server in the order the bots join, so the same seed gives the same run.
        await link.IdleAsync();
    }

    private async Task ActAsync()
    {
        for (var i = 0; i < _bots.Count; i++)
        {
            _bots[i].Act();

            // Links deliver on their own threads. Each bot's message lands before the next bot acts,
            // so the server reads them in a fixed order.
            await _links[i].IdleAsync();
        }
    }

    /// <summary>A message to one bot's link can make the host send to another, so it goes round twice.</summary>
    // shortcut: two rounds cover one hop between links. Loop until no link has work when a message takes a third.
    private async Task DeliverAsync()
    {
        for (var round = 0; round < 2; round++)
        {
            foreach (var link in _links)
            {
                await link.IdleAsync();
            }
        }
    }

    private sealed class NoPacer : IPacer
    {
        public Task UntilNextAsync() => Task.CompletedTask;
    }
}
