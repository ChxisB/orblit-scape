using System.Buffers.Binary;
using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>An object standing in the world.</summary>
public sealed class PlacedObject
{
    internal PlacedObject(ulong entity, ulong networkId, ObjectDef def, Footprint footprint)
    {
        Entity = entity;
        NetworkId = networkId;
        Def = def;
        Footprint = footprint;
    }

    public ulong Entity { get; }

    public ulong NetworkId { get; }

    public ObjectDef Def { get; }

    public Footprint Footprint { get; }

    /// <summary>Where climbing it leads, for an object that has such a way.</summary>
    public TileCoord? Destination { get; internal init; }

    public int State { get; internal set; }
}

/// <summary>
/// The objects of the world: trees, doors, stairs. Each is an entity the clients near it draw. A
/// state change updates the entity and the flags of its tiles together, so a door that opens can
/// be walked through.
/// </summary>
public sealed class WorldObjects
{
    private readonly Services _services;
    private readonly ObjectSet _objects;
    private readonly ComponentType _position;
    private readonly ComponentType _object;
    private readonly Dictionary<ulong, PlacedObject> _placed = [];
    private readonly Dictionary<TileCoord, PlacedObject> _byTile = [];

    private WorldObjects(Services services, ObjectSet objects)
    {
        _services = services;
        _objects = objects;
        _position = services.World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        _object = services.World.RegisterComponent(WorldObject.Name, ComponentKind.Int32, WorldObject.Ints);
    }

    /// <summary>
    /// Adds objects to <paramref name="registry"/> and returns the feature, so a feature that owns
    /// a kind of object can place it and change its state.
    /// </summary>
    public static WorldObjects Register(Registry registry, Services services, ObjectSet objects)
    {
        var feature = new WorldObjects(services, objects);
        registry.Replicate(feature._object);
        return feature;
    }

    /// <summary>The object with <paramref name="networkId"/>, or null when there is none.</summary>
    public PlacedObject? Find(ulong networkId) => _placed.GetValueOrDefault(networkId);

    /// <summary>The object standing on <paramref name="tile"/>, or null.</summary>
    public PlacedObject? At(TileCoord tile) => _byTile.GetValueOrDefault(tile);

    /// <summary>Stands the object <paramref name="id"/> with its south-west tile on <paramref name="origin"/>.</summary>
    /// <returns>The object, which is how a caller finds what it just placed.</returns>
    /// <exception cref="ArgumentException">The object does not exist, or a tile is blocked, water or taken.</exception>
    public PlacedObject Place(int id, TileCoord origin, TileCoord? destination = null)
    {
        var def = _objects.Require(id);
        var footprint = def.FootprintAt(origin);
        if (!IsFree(footprint))
        {
            throw new ArgumentException($"{origin} is not free ground.", nameof(origin));
        }

        var world = _services.World;
        var entity = world.CreateEntity();
        world.Add(entity, _position);
        world.Add(entity, _object);
        if (world.TryGetBytes(entity, _position, out var position))
        {
            TilePosition.Write(position, origin);
        }

        var placed = new PlacedObject(entity, _services.Hosting.Spawn(entity), def, footprint) { Destination = destination };
        _placed[placed.NetworkId] = placed;
        foreach (var tile in footprint.Tiles)
        {
            _byTile[tile] = placed;
        }

        Write(placed);
        SetBlocking(placed, def.BlocksIn(placed.State));
        return placed;
    }

    /// <summary>
    /// Puts <paramref name="placed"/> in <paramref name="state"/>, which its clients are told and
    /// which changes whether its tiles block.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The state does not fit an int32.</exception>
    public void SetState(PlacedObject placed, long state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(state, int.MaxValue);
        placed.State = (int)state;
        Write(placed);
        SetBlocking(placed, placed.Def.BlocksIn(placed.State));
    }

    private bool IsFree(Footprint footprint) =>
        footprint.Tiles.All(tile =>
            !_services.Map.FlagsAt(tile).Has(TileFlags.Blocked | TileFlags.Water) && !_byTile.ContainsKey(tile));

    private void Write(PlacedObject placed)
    {
        if (_services.World.TryGetBytes(placed.Entity, _object, out var bytes))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, placed.Def.Id);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[sizeof(int)..], placed.State);
        }
    }

    private void SetBlocking(PlacedObject placed, bool blocks)
    {
        var map = _services.Map;
        foreach (var tile in placed.Footprint.Tiles)
        {
            var flags = map.FlagsAt(tile);
            map.SetFlags(tile, blocks ? flags | TileFlags.Blocked : flags.Without(TileFlags.Blocked));
        }
    }
}
