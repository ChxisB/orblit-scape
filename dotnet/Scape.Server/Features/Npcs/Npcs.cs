using System.Buffers.Binary;
using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>A creature standing in the world.</summary>
public sealed class PlacedNpc
{
    internal PlacedNpc(NpcDef def, TileCoord home, int radius)
    {
        Def = def;
        Home = home;
        Radius = radius;
        Leash = def.Behaviour is Aggressive aggressive ? Math.Max(aggressive.Leash, radius) : radius;
        Mover = new Mover(home, Walker.Npc);
    }

    public NpcDef Def { get; }

    /// <summary>Where it was placed, and where it goes back to.</summary>
    public TileCoord Home { get; }

    /// <summary>How far from <see cref="Home"/> it wanders.</summary>
    public int Radius { get; }

    /// <summary>How far from <see cref="Home"/> it ever goes.</summary>
    public int Leash { get; }

    /// <summary>The entity standing in the world now.</summary>
    public ulong Entity { get; internal set; }

    public ulong NetworkId { get; internal set; }

    /// <summary>
    /// How many tiles it entered on the last creature phase. A creature that did not take part in
    /// it entered none.
    /// </summary>
    public int StepsLastTick { get; internal set; }

    /// <summary>The tile it stands on.</summary>
    public TileCoord Tile => Mover.Position;

    /// <summary>Whether it is dead and waiting to stand again.</summary>
    public bool IsDead => RespawnAt is not null;

    /// <summary>Whether it is in a fight, which keeps it where it stands.</summary>
    public bool IsEngaged { get; internal set; }

    internal Mover Mover { get; }

    internal bool Asleep { get; set; }

    internal bool Hunting { get; set; }

    internal long? RespawnAt { get; set; }
}

/// <summary>What creatures ask of the rest of the server.</summary>
public sealed record NpcsOptions
{
    public required NpcSet Npcs { get; init; }

    /// <summary>The tile each player in the world stands on.</summary>
    public required Func<IReadOnlyList<TileCoord>> PlayerTiles { get; init; }

    public required Random Random { get; init; }
}

/// <summary>
/// The creatures of the world. They walk on the creature phase, and only while a player is near
/// enough to make their home cell active.
/// </summary>
/// <remarks>
/// A creature whose cell was asleep is put back at its home tile when the cell wakes, so a region
/// nobody visited never holds a creature mid walk.
/// </remarks>
public sealed class Npcs
{
    /// <summary>The chance each tick that a creature with nothing to do sets off.</summary>
    public const double WanderChance = 0.1;

    /// <summary>Ticks a creature that died stays in view, so its death can be shown.</summary>
    public const int DeathTicks = 3;

    private readonly ActionQueue _actions;
    private readonly Services _services;
    private readonly NpcsOptions _options;
    private readonly ComponentType _position;
    private readonly ComponentType _npc;
    private readonly List<PlacedNpc> _all = [];
    private readonly OrderedDictionary<ulong, PlacedNpc> _standing = [];
    private readonly List<(ulong Entity, long Until)> _dying = [];
    private readonly List<Action<PlacedNpc>> _standHandlers = [];

    private Npcs(ActionQueue actions, Services services, NpcsOptions options)
    {
        _actions = actions;
        _services = services;
        _options = options;
        _position = services.World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        _npc = services.World.RegisterComponent(Npc.Name, ComponentKind.Int32, Npc.Ints);
    }

    /// <summary>
    /// Adds creatures to <paramref name="registry"/> and returns the feature, so a feature that
    /// places creatures can spawn them.
    /// </summary>
    public static Npcs Register(Registry registry, Services services, NpcsOptions options)
    {
        var feature = new Npcs(registry.Actions, services, options);
        registry.Replicate(feature._npc);
        registry.AddSystem(Phase.Npcs, feature.Step);
        return feature;
    }

    /// <summary>How many creatures are placed, dead ones included.</summary>
    public int Count => _all.Count;

    /// <summary>The creatures standing in the world now.</summary>
    public IEnumerable<PlacedNpc> Standing => _standing.Values;

    /// <summary>The creature standing with <paramref name="networkId"/>, or null.</summary>
    public PlacedNpc? Find(ulong networkId) => _standing.GetValueOrDefault(networkId);

    /// <summary>
    /// Calls <paramref name="handler"/> each time a creature enters the world, at its spawn and
    /// when it returns from the dead.
    /// </summary>
    public void AddStandHandler(Action<PlacedNpc> handler) => _standHandlers.Add(handler);

    /// <summary>
    /// Takes <paramref name="npc"/> out of the world until its respawn time has passed and a
    /// player is near. It stays in view for <see cref="DeathTicks"/>, or until it would stand
    /// again if that is sooner, and nothing can act on it meanwhile. Does nothing for a creature
    /// already dead.
    /// </summary>
    public void Kill(PlacedNpc npc)
    {
        if (npc.IsDead)
        {
            return;
        }

        _standing.Remove(npc.NetworkId);
        var tick = _actions.Tick;
        var respawnAt = tick + (npc.Def.Fight?.RespawnTicks ?? 1);
        _dying.Add((npc.Entity, Math.Min(tick + DeathTicks, respawnAt)));
        npc.RespawnAt = respawnAt;
        npc.IsEngaged = false;
        npc.Hunting = false;
    }

    /// <summary>Puts a creature in the world.</summary>
    /// <returns>The creature, which is how a caller finds what it just placed.</returns>
    /// <exception cref="ArgumentException">The creature does not exist, or may not stand on the tile.</exception>
    public PlacedNpc Spawn(NpcSpawn spawn)
    {
        var def = _options.Npcs.Require(spawn.Npc);
        if (_services.Map.FlagsAt(spawn.Tile).Has(TileFlags.Blocked | TileFlags.Water | TileFlags.NoNpc))
        {
            throw new ArgumentException($"{spawn.Tile} is not open to creatures.", nameof(spawn));
        }

        var placed = new PlacedNpc(def, spawn.Tile, spawn.Radius);
        Stand(placed);
        _all.Add(placed);
        return placed;
    }

    /// <summary>The distance in tiles, counting a diagonal as one.</summary>
    private static int Gap(TileCoord a, TileCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    // A creature notices at its sight and keeps on to its leash.
    private static TileCoord? PreyOf(PlacedNpc npc, IReadOnlyList<TileCoord> players) =>
        npc.Def.Behaviour is Aggressive aggressive
            ? Nearest(npc, players, npc.Hunting ? aggressive.Leash : aggressive.Sight, aggressive.Leash)
            : null;

    private static TileCoord? Nearest(PlacedNpc npc, IReadOnlyList<TileCoord> players, int sight, int leash)
    {
        TileCoord? nearest = null;
        foreach (var player in players)
        {
            var seen = player.Plane == npc.Home.Plane && Gap(npc.Tile, player) <= sight && Gap(npc.Home, player) <= leash;
            if (seen && (nearest is not { } best || Gap(npc.Tile, player) < Gap(npc.Tile, best)))
            {
                nearest = player;
            }
        }

        return nearest;
    }

    /// <summary>The steps of <paramref name="steps"/> up to the first that leaves the leash.</summary>
    private static List<TileCoord> Inside(PlacedNpc npc, IReadOnlyList<TileCoord> steps) =>
        [.. steps.TakeWhile(step => Gap(npc.Home, step) <= npc.Leash)];

    private void Stand(PlacedNpc npc)
    {
        var world = _services.World;
        var entity = world.CreateEntity();
        world.Add(entity, _position);
        world.Add(entity, _npc);
        if (world.TryGetBytes(entity, _npc, out var bytes))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, npc.Def.Id);
        }

        npc.Entity = entity;
        npc.NetworkId = _services.Hosting.Spawn(entity);
        npc.RespawnAt = null;
        _standing[npc.NetworkId] = npc;
        Write(npc);
        foreach (var handler in _standHandlers)
        {
            handler(npc);
        }
    }

    private void Step()
    {
        ClearDead();
        var players = _options.PlayerTiles();
        foreach (var npc in _all)
        {
            npc.StepsLastTick = 0;
            if (npc.IsDead)
            {
                MaybeReturn(npc);
            }
            else if (!IsActive(npc))
            {
                npc.Asleep = true;
            }
            else if (npc.Asleep)
            {
                Wake(npc);
            }
            else
            {
                Move(npc, players);
            }
        }
    }

    private void ClearDead()
    {
        var tick = _actions.Tick;
        foreach (var (entity, _) in _dying.Where(dying => dying.Until <= tick))
        {
            _services.Hosting.Despawn(entity);
        }

        _dying.RemoveAll(dying => dying.Until <= tick);
    }

    private bool IsActive(PlacedNpc npc) => _services.Active.IsActiveAt(npc.Home.X, npc.Home.Y);

    private void MaybeReturn(PlacedNpc npc)
    {
        if (npc.RespawnAt > _actions.Tick || !IsActive(npc))
        {
            return;
        }

        npc.Mover.ClearPath();
        npc.Mover.Position = npc.Home;
        Stand(npc);
    }

    private void Wake(PlacedNpc npc)
    {
        npc.Asleep = false;
        npc.Mover.ClearPath();
        npc.Mover.Position = npc.Home;
        Write(npc);
    }

    private void Move(PlacedNpc npc, IReadOnlyList<TileCoord> players)
    {
        Decide(npc, players);
        npc.Mover.Advance(_services.Map);
        npc.StepsLastTick = npc.Mover.StepsLastTick.Count;
        if (npc.StepsLastTick > 0)
        {
            Write(npc);
        }
    }

    private void Decide(PlacedNpc npc, IReadOnlyList<TileCoord> players)
    {
        if (PreyOf(npc, players) is { } prey)
        {
            Chase(npc, prey);
        }
        else if (npc.IsEngaged)
        {
            npc.Mover.ClearPath();
        }
        else if (npc.Hunting)
        {
            npc.Hunting = false;
            WalkTo(npc, npc.Home);
        }
        else if (!npc.Mover.IsMoving)
        {
            Roam(npc);
        }
    }

    private void Chase(PlacedNpc npc, TileCoord prey)
    {
        npc.Hunting = true;
        var reach = new Beside(Footprint.OfTile(prey));
        if (reach.IsSatisfied(_services.Map, npc.Tile))
        {
            npc.Mover.ClearPath();
            return;
        }

        var path = PathSearch.FindToReach(_services.Map, npc.Tile, reach, Walker.Npc);
        npc.Mover.SetPath(Inside(npc, path.Steps));
    }

    private void Roam(PlacedNpc npc)
    {
        if (Gap(npc.Tile, npc.Home) > npc.Radius)
        {
            WalkTo(npc, npc.Home);
        }
        else if (npc.Def.Behaviour is not Idle && _options.Random.NextDouble() < WanderChance)
        {
            WalkTo(npc, AroundHome(npc));
        }
    }

    private TileCoord AroundHome(PlacedNpc npc)
    {
        var x = Math.Clamp(npc.Home.X + Offset(npc.Radius), 0, TileWorld.WorldSize - 1);
        var y = Math.Clamp(npc.Home.Y + Offset(npc.Radius), 0, TileWorld.WorldSize - 1);
        return new TileCoord(x, y, npc.Home.Plane);
    }

    private int Offset(int radius) => (int)Math.Floor(_options.Random.NextDouble() * ((2 * radius) + 1)) - radius;

    private void WalkTo(PlacedNpc npc, TileCoord target)
    {
        var path = PathSearch.Find(_services.Map, npc.Tile, target, Walker.Npc);
        npc.Mover.SetPath(Inside(npc, path.Steps));
    }

    private void Write(PlacedNpc npc)
    {
        if (_services.World.TryGetBytes(npc.Entity, _position, out var bytes))
        {
            TilePosition.Write(bytes, npc.Tile);
        }
    }
}
