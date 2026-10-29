using Orblit.Core;
using Orblit.Net;
using Scape.Core;

namespace Scape.Server;

/// <summary>What a server is built from.</summary>
public sealed record ScapeServerOptions
{
    public required TickClock Clock { get; init; }

    public required IPacer Pacer { get; init; }

    public required Action<string> Log { get; init; }

    public required TileMap Map { get; init; }

    public required TileCoord Spawn { get; init; }

    public required ItemSet Items { get; init; }

    public required ObjectSet Objects { get; init; }

    public required NpcSet Npcs { get; init; }

    public required SkillSet Skills { get; init; }

    public required NodeSet Nodes { get; init; }

    /// <summary>What players can make. A server without recipes makes nothing.</summary>
    public RecipeSet? Recipes { get; init; }

    /// <summary>What shops charge and pay. Required when a creature keeps a shop.</summary>
    public ShopRules? Shops { get; init; }

    public required CombatRules Combat { get; init; }

    /// <summary>What players can cast. A server without spells casts nothing.</summary>
    public SpellSet? Spells { get; init; }

    /// <summary>What players can pray for. A server without prayers has none.</summary>
    public PrayerSet? Prayers { get; init; }

    /// <summary>Where creatures, drops, gathering and making get their chance. A test passes a seeded one.</summary>
    public Random Random { get; init; } = new();
}

/// <summary>The world, the host and the loop, wired together.</summary>
public sealed class ScapeServer : IDisposable
{
    /// <summary>The bytes the net layer adds round a message before the host counts it.</summary>
    private const int WrapperBytes = 4;

    /// <summary>Ticks a connection may stay silent before it must log in, 12 seconds.</summary>
    private const int LoginTicks = 20;

    /// <summary>Ticks a dropped player's avatar stays in the world, 6 seconds.</summary>
    private const int PlayerLingerTicks = 10;

    /// <summary>
    /// Ticks a dropped player who is in a fight stays, 60 seconds, so pulling the plug is not a
    /// way out of one.
    /// </summary>
    private const int FightingLingerTicks = 100;

    /// <summary>Interest and active cells share one grid of 16 tiles a side.</summary>
    private const int CellTiles = 16;

    /// <summary>A player is told about the cell they stand in and the ring round it.</summary>
    private const int InterestRing = 1;

    /// <summary>A cell within this many cells of a player is simulated.</summary>
    private const int ActiveRadius = 1;

    /// <summary>Ticks a cell stays simulated once no player is near it.</summary>
    private const int ActiveHoldTicks = 10;

    public ScapeServer(ScapeServerOptions options)
    {
        var networkId = World.RegisterComponent("NetworkId", ComponentKind.Int64);
        var bridge = new HostBridge();
        var grid = new Grid(CellTiles);
        Active = new ActiveCells(grid, ActiveRadius, GameTick.Period * ActiveHoldTicks, () => GameTick.Period * Registry.Actions.Tick);
        Registry.AddSystem(Phase.Inbound, DeliverIntents);
        var features = Features.Register(Registry, new Services(World, options.Map, bridge, Active), options);
        Movement = features.Movement;
        Social = features.Social;
        Items = features.Items;
        Ground = features.Ground;
        Npcs = features.Npcs;
        Actors = features.Actors;
        Objects = features.Objects;
        Skills = features.Skills;
        Interactions = features.Interactions;
        Combat = features.Combat;
        Prayers = features.Prayers;
        Wire = new ClientWire(bridge, Inbox);
        var set = new ReplicationSet(Registry.Replicated);
        var nearby = new GridInterest(set, Registry.Replicated.First(c => c.Name == TilePosition.Name), grid, InterestRing);
        Registry.AddSystem(Phase.Inbound, () => Active.Update(nearby.FocusCells));
        Host = new NetHost(new NetHostOptions
        {
            World = World,
            Set = set,
            NetworkId = networkId,
            Clock = options.Clock,
            Interest = new FilteredInterest(nearby, Ground.CanSee),
            OnOverrun = summary => options.Log(SummaryText.DescribeLateTick(summary)),
            Sessions = Sessions(),
            Budget = new InboundBudget(
                messagesPerTick: 20,
                bytesPerMessage: MessageCodec.ClientMessageLimit + WrapperBytes,
                bytesPerTick: 4096),
            OnMessage = Wire.OnMessage,
        });
        bridge.Host = Host;
        Loop = new TickLoop(Host, Registry, options.Pacer);
    }

    public World World { get; } = new();

    public Registry Registry { get; } = new();

    public Inbox Inbox { get; } = new();

    public ActiveCells Active { get; }

    public Movement Movement { get; }

    public Social Social { get; }

    public Items Items { get; }

    public GroundItems Ground { get; }

    public Npcs Npcs { get; }

    public Actors Actors { get; }

    public WorldObjects Objects { get; }

    public Skills Skills { get; }

    public Interactions Interactions { get; }

    public Combat Combat { get; }

    public Prayers? Prayers { get; }

    public ClientWire Wire { get; }

    public NetHost Host { get; }

    public TickLoop Loop { get; }

    public void Dispose()
    {
        Host.Dispose();
        World.Dispose();
    }

    private SessionPolicy Sessions() => new()
    {
        Admit = Wire.Admit,
        OnJoin = key =>
        {
            Wire.OnJoin(key);
            Registry.PlayerJoined(key);
        },
        OnGone = (key, _) =>
        {
            Wire.OnGone(key);
            Registry.PlayerGone(key);
        },
        JoinTicks = LoginTicks,
        LingerTicks = PlayerLingerTicks,
        LingerFor = key => Combat.IsFighting(key) ? FightingLingerTicks : PlayerLingerTicks,
    };

    private void DeliverIntents()
    {
        foreach (var (player, intent) in Inbox.Drain())
        {
            Registry.Deliver(player, intent);
        }
    }

    /// <summary>
    /// What the wire and the features reach the host through. The host is built after the features
    /// register, so it arrives late.
    /// </summary>
    private sealed class HostBridge : IWireLink, IHosting
    {
        // Set in the server's constructor, before any client can reach the bridge.
        public NetHost Host { get; set; } = null!;

        public bool IsLive(string key) => Host.SessionStateOf(key) == SessionState.Live;

        public void Send(string key, byte[] body) => Host.SendMessage(key, body);

        public ulong Spawn(ulong entity, string? owner = null) => Host.Spawn(entity, owner);

        public void Despawn(ulong entity) => Host.Despawn(entity);

        public void Tell(string player, ServerMessage message) => Send(player, MessageCodec.Encode(message));
    }
}
