using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// Players that walk. A walk intent finds a path on the action tick, and the movement phase takes
/// the steps and writes where each player stands.
/// </summary>
public sealed class Movement
{
    private readonly Registry _registry;
    private readonly Services _services;
    private readonly TileCoord _spawn;
    private readonly ComponentType _position;
    private readonly OrderedDictionary<string, Player> _players = [];
    private readonly Dictionary<ulong, string> _byNetworkId = [];
    private readonly ActionKind _walk = new("walk", cancels: new CancelsAll());

    private Movement(Registry registry, Services services, TileCoord spawn, ComponentType position)
    {
        _registry = registry;
        _services = services;
        _spawn = spawn;
        _position = position;
    }

    /// <summary>
    /// Adds walking to <paramref name="registry"/> and returns the feature, so a test or a later
    /// feature can ask where a player stands.
    /// </summary>
    public static Movement Register(Registry registry, Services services, TileCoord spawn)
    {
        if (services.Map.FlagsAt(spawn).Has(TileFlags.Blocked | TileFlags.Water))
        {
            throw new ArgumentException($"{spawn} is not open ground.", nameof(spawn));
        }

        var position = services.World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        registry.Replicate(position);
        var movement = new Movement(registry, services, spawn, position);
        registry.AddKind(movement._walk);
        registry.AddJoinHandler(movement.Join);
        registry.AddGoneHandler(movement.Gone);
        registry.AddIntentHandler(movement.OnIntent);
        registry.AddSystem(Phase.Movement, movement.Step);
        return movement;
    }

    /// <summary>The key of each player in the world.</summary>
    public IEnumerable<string> Players => _players.Keys;

    /// <summary>The tile each player in the world stands on.</summary>
    public IReadOnlyList<TileCoord> Tiles => [.. _players.Values.Select(player => player.Mover.Position)];

    /// <summary>The tile <paramref name="player"/> stands on, or null when they are not in the world.</summary>
    public TileCoord? TileOf(string player) => _players.TryGetValue(player, out var found) ? found.Mover.Position : null;

    /// <summary>The player whose avatar has <paramref name="networkId"/>, or null.</summary>
    public string? PlayerWith(ulong networkId) => _byNetworkId.GetValueOrDefault(networkId);

    /// <summary>
    /// The key actions of <paramref name="player"/> are queued under, or null when they are not in
    /// the world.
    /// </summary>
    public ulong? ActorOf(string player) => _players.TryGetValue(player, out var found) ? found.Entity : null;

    public bool IsMoving(string player) => _players.TryGetValue(player, out var found) && found.Mover.IsMoving;

    /// <summary>How many tiles <paramref name="player"/> entered on the last movement phase.</summary>
    public int StepsLastTick(string player) =>
        _players.TryGetValue(player, out var found) ? found.Mover.StepsLastTick.Count : 0;

    /// <summary>
    /// Sets <paramref name="player"/> walking along <paramref name="steps"/>, each beside the one
    /// before, or stops them when it is empty.
    /// </summary>
    public void Follow(string player, IReadOnlyList<TileCoord> steps)
    {
        if (_players.TryGetValue(player, out var found))
        {
            found.Mover.SetPath(steps);
        }
    }

    /// <summary>Puts <paramref name="player"/> on <paramref name="tile"/> with no walk between, such as up a stair.</summary>
    public void Teleport(string player, TileCoord tile)
    {
        if (!_players.TryGetValue(player, out var found))
        {
            return;
        }

        found.Mover.ClearPath();
        found.Mover.Position = tile;
        Write(found.Entity, tile);
    }

    /// <summary>
    /// The replicated entity and the tile of <paramref name="player"/>, or null when they are not
    /// in the world.
    /// </summary>
    public (ulong NetworkId, TileCoord Tile)? Standing(string player) =>
        _players.TryGetValue(player, out var found) ? (found.NetworkId, found.Mover.Position) : null;

    private void Join(string key)
    {
        var world = _services.World;
        var entity = world.CreateEntity();
        world.Add(entity, _position);
        var networkId = _services.Hosting.Spawn(entity, owner: key);
        _players[key] = new Player(entity, networkId, new Mover(_spawn));
        _byNetworkId[networkId] = key;
        Write(entity, _spawn);
        _services.Hosting.Tell(key, new Avatar((long)networkId));
    }

    private void Gone(string key)
    {
        if (!_players.Remove(key, out var player))
        {
            return;
        }

        _byNetworkId.Remove(player.NetworkId);
        _services.Hosting.Despawn(player.Entity);
    }

    private void OnIntent(string key, Intent intent)
    {
        if (!_players.TryGetValue(key, out var player) || intent is not Walk walk)
        {
            return;
        }

        _registry.Actions.Submit(player.Entity, _walk, () => StartWalk(player, walk.Target));
    }

    private void StartWalk(Player player, TileCoord target)
    {
        var from = player.Mover.Position;
        if (target.Plane != from.Plane)
        {
            return;
        }

        player.Mover.SetPath(PathSearch.Find(_services.Map, from, target).Steps);
    }

    private void Step()
    {
        foreach (var player in _players.Values)
        {
            player.Mover.Advance(_services.Map);
            if (player.Mover.StepsLastTick.Count > 0)
            {
                Write(player.Entity, player.Mover.Position);
            }
        }
    }

    private void Write(ulong entity, TileCoord tile)
    {
        if (_services.World.TryGetBytes(entity, _position, out var bytes))
        {
            TilePosition.Write(bytes, tile);
        }
    }

    private sealed record Player(ulong Entity, ulong NetworkId, Mover Mover);
}
