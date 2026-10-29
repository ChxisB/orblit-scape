using Orblit.Core;
using Orblit.Net;
using Scape.Core;

namespace Scape.Server;

/// <summary>What a server is built from.</summary>
public sealed record ScapeServerOptions
{
    public required TickClock Clock { get; init; }

    public required IPacer Pacer { get; init; }

    /// <summary>Takes a line for the operator. A login that fails or a save that is not written logs from another thread.</summary>
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

    /// <summary>The quests and conversations. Required when a creature has a dialogue.</summary>
    public Story? Story { get; init; }

    public required CombatRules Combat { get; init; }

    /// <summary>What players can cast. A server without spells casts nothing.</summary>
    public SpellSet? Spells { get; init; }

    /// <summary>What players can pray for. A server without prayers has none.</summary>
    public PrayerSet? Prayers { get; init; }

    /// <summary>Where creatures, drops, gathering and making get their chance. A test passes a seeded one.</summary>
    public Random Random { get; init; } = new();

    /// <summary>Where players are kept between visits. A server without it forgets them when they leave.</summary>
    public Storage? Storage { get; init; }

    /// <summary>
    /// The moderators and admins, by name. Anyone else is a player. Roles need accounts in
    /// <see cref="Storage"/>, since a name proves nothing without a password.
    /// </summary>
    public IReadOnlyDictionary<string, Role> Roles { get; init; } = new Dictionary<string, Role>();
}

/// <summary>The world, the host and the loop, wired together.</summary>
public sealed class ScapeServer : IAsyncDisposable
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
        if (options.Roles.Count > 0 && options.Storage?.Hasher is null)
        {
            throw new ArgumentException("Roles need accounts, since a name proves nothing without a password.", nameof(options));
        }

        var networkId = World.RegisterComponent("NetworkId", ComponentKind.Int64);
        var bridge = new HostBridge();
        var grid = new Grid(CellTiles);
        Active = new ActiveCells(grid, ActiveRadius, GameTick.Period * ActiveHoldTicks, () => GameTick.Period * Registry.Actions.Tick);
        Registry.AddSystem(Phase.Inbound, DeliverIntents);
        var services = new Services(World, options.Map, bridge, Active);
        var features = Features.Register(Registry, services, options, (a, b) => Saves?.SaveTogether(a, b));
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
        Quests = features.Quests;
        Func<string, Login, Task<LoginRefusal?>>? gate = null;
        if (options.Storage is { } storage)
        {
            var ledger = new Ledger(storage.Queue, storage.Now);
            Admin.Register(Registry, services, new AdminOptions
            {
                Roles = options.Roles.ToDictionary(r => Scape.Core.Wire.NameKey(r.Key), r => r.Value),
                Ledger = ledger,
                Social = features.Social,
                Movement = features.Movement,
                Skills = features.Skills,
                SkillSet = options.Skills,
                Bags = features.Items,
                Items = options.Items,
                Rollback = RollbackOf(storage, ledger, bridge),
                // The loop is built at the end of the constructor, before any tick can run a countdown.
                Stop = () => Loop!.Stop(),
                Log = options.Log,
            });
            Saves = RegisterSaves(storage, features, options);
            gate = new Entrance(Saves.PrepareAsync, AccountsOf(storage), ledger).CheckAsync;
        }

        Wire = new ClientWire(bridge, Inbox, options.Log, gate);
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

    /// <summary>Every player's quests, or null for a server without a story.</summary>
    public Quests? Quests { get; }

    public ClientWire Wire { get; }

    public NetHost Host { get; }

    public TickLoop Loop { get; }

    /// <summary>Every player's save, or null for a server without storage.</summary>
    internal Saves? Saves { get; }

    /// <summary>
    /// Puts <paramref name="objects"/> and <paramref name="npcs"/> in the world. Call it once,
    /// before players join. A throw leaves the placements before it in the world, and a second
    /// call adds the creatures again.
    /// </summary>
    /// <exception cref="ArgumentException">The world refuses a placement, as <see cref="WorldObjects.Place"/> and <see cref="Npcs.Spawn"/> do.</exception>
    public void Stock(IEnumerable<ObjectPlacement> objects, IEnumerable<NpcSpawn> npcs)
    {
        foreach (var placement in objects)
        {
            Objects.Place(placement.ObjectId, placement.Tile, placement.Destination);
        }

        foreach (var spawn in npcs)
        {
            Npcs.Spawn(spawn);
        }
    }

    /// <summary>Stops the host, saves every player in the world and waits for the store. Call it after the loop stops.</summary>
    public async ValueTask DisposeAsync()
    {
        // The host goes first, so no join or leave changes the world while it is saved.
        Host.Dispose();
        if (Saves is { } saves)
        {
            await saves.ShutdownAsync();
        }

        World.Dispose();
    }

    /// <summary>The rollback an admin command runs, or null when the store keeps no history to roll back to.</summary>
    private static Func<string, int, Task<string>>? RollbackOf(Storage storage, Ledger ledger, HostBridge bridge) =>
        storage.History is { } history
            ? new Rollback(new RollbackOptions
            {
                History = history,
                Queue = storage.Queue,
                Ledger = ledger,
                LogOut = bridge.Kick,
                IsOnline = key => bridge.Host.SessionStateOf(key) is not null,
                Now = storage.Now,
            }).RunAsync
            : null;

    private static Accounts? AccountsOf(Storage storage) =>
        storage.Hasher is { } hasher ? new Accounts(storage.Queue, hasher, storage.Salt) : null;

    private SessionPolicy Sessions() => new()
    {
        Admit = Wire.Admit,
        OnJoin = key =>
        {
            Wire.OnJoin(key);
            Registry.PlayerJoined(key);
            Saves?.Arrived(key);
        },
        OnGone = (key, reason) =>
        {
            Saves?.Left(key, reason);
            Wire.OnGone(key);
            Registry.PlayerGone(key);
        },
        JoinTicks = LoginTicks,
        LingerTicks = PlayerLingerTicks,
        LingerFor = key => Combat.IsFighting(key) ? FightingLingerTicks : PlayerLingerTicks,
    };

    /// <summary>Builds the saves kept in <paramref name="storage"/> and saves on the tick.</summary>
    private Saves RegisterSaves(Storage storage, Features features, ScapeServerOptions options)
    {
        var saves = new Saves(new SavesOptions
        {
            Queue = storage.Queue,
            States = new PlayerStates(features, options.Map),
            AutoSaver = storage.AutoSaver,
            SessionOf = key => Host.SessionStateOf(key),
            Log = options.Log,
        });
        Registry.AddSystem(Phase.Outbound, () =>
        {
            saves.Autosave(Registry.Actions.Tick);
            saves.Sweep();
        });
        return saves;
    }

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

        // The host's lock is held through a tick, so the removal waits for the tick to end rather
        // than pull a player out from under the features in the middle of one.
        public Task Kick(string player) => Task.Run(() => Host.RemoveClient(player));
    }
}
