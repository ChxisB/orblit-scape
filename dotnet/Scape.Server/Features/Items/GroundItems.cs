using System.Buffers.Binary;
using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>An item lying on the ground, as a query answers it.</summary>
public readonly record struct Lying(ulong NetworkId, int Item, int Count);

/// <summary>What ground items ask of the rest of the server.</summary>
public sealed record GroundItemsOptions
{
    public required ItemSet Items { get; init; }

    /// <summary>The tile a player stands on, or null when they are not in the world.</summary>
    public required Func<string, TileCoord?> TileOf { get; init; }

    /// <summary>The inventory of a player, or null when they are not in the world.</summary>
    public required Func<string, Container?> InventoryOf { get; init; }
}

/// <summary>
/// Items lying on tiles. Each is an entity the clients near it draw, with a timer: first it
/// belongs to one player and the others are not told about it, then it belongs to everyone, then
/// it vanishes.
/// </summary>
public sealed class GroundItems
{
    /// <summary>Ticks an item belongs to the player who dropped or earned it, 60 seconds.</summary>
    public const int OwnerTicks = 100;

    /// <summary>Ticks it then lies where anyone may take it before it vanishes.</summary>
    public const int PublicTicks = 100;

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly GroundItemsOptions _options;
    private readonly ComponentType _position;
    private readonly ComponentType _ground;
    private readonly OrderedDictionary<ulong, Dropped> _lying = [];

    private GroundItems(Registry registry, Services services, GroundItemsOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
        _position = services.World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        _ground = services.World.RegisterComponent(GroundItem.Name, ComponentKind.Int32, GroundItem.Ints);
    }

    /// <summary>
    /// Adds ground items to <paramref name="registry"/> and returns the feature, so loot and
    /// gathering can put items down and an interaction can pick them up.
    /// </summary>
    public static GroundItems Register(Registry registry, Services services, GroundItemsOptions options)
    {
        var feature = new GroundItems(registry, services, options);
        registry.Replicate(feature._ground);
        registry.AddSystem(Phase.Outbound, feature.Expire);
        return feature;
    }

    /// <summary>
    /// Whether <paramref name="player"/> may be told about <paramref name="networkId"/>. Anything
    /// that is not an item on the ground, and any item no longer owned, is open to all.
    /// </summary>
    public bool CanSee(string player, ulong networkId) =>
        !_lying.TryGetValue(networkId, out var item) || item.Owner is null || item.Owner == player;

    /// <summary>
    /// The tile the item <paramref name="networkId"/> lies on, or null when there is no such item
    /// or <paramref name="player"/> may not see it.
    /// </summary>
    public TileCoord? WhereIs(string player, ulong networkId) =>
        CanSee(player, networkId) && _lying.TryGetValue(networkId, out var item) ? item.Tile : null;

    /// <summary>What <paramref name="player"/> can see on <paramref name="tile"/>.</summary>
    public IReadOnlyList<Lying> LyingAt(string player, TileCoord tile) =>
    [
        .. _lying.Values
            .Where(item => item.Tile == tile && CanSee(player, item.NetworkId))
            .Select(item => new Lying(item.NetworkId, item.Item, item.Count)),
    ];

    /// <summary>
    /// Puts <paramref name="count"/> of <paramref name="item"/> on <paramref name="at"/>. With an
    /// <paramref name="owner"/> only that player is told of it until the owner timer ends.
    /// </summary>
    /// <returns>The entity's network id, which is how a caller finds the item it just put down.</returns>
    /// <exception cref="ArgumentException">The item does not exist or the count is out of range.</exception>
    public ulong Drop(int item, int count, TileCoord at, string? owner = null)
    {
        var stack = new ItemStack(_options.Items.Require(item).Id, count);
        var now = _registry.Actions.Tick;
        var ownedUntil = owner is null ? now : now + OwnerTicks;
        var world = _services.World;
        var entity = world.CreateEntity();
        world.Add(entity, _position);
        world.Add(entity, _ground);
        if (world.TryGetBytes(entity, _position, out var position))
        {
            TilePosition.Write(position, at);
        }

        WriteGround(entity, 0, stack.Item);
        WriteGround(entity, 1, stack.Count);
        var networkId = _services.Hosting.Spawn(entity);
        _lying[networkId] = new Dropped
        {
            Entity = entity,
            NetworkId = networkId,
            Item = stack.Item,
            Count = stack.Count,
            Tile = at,
            Owner = owner,
            OwnedUntil = ownedUntil,
        };
        return networkId;
    }

    /// <summary>
    /// Drops <paramref name="count"/> of the stack in <paramref name="slot"/> of
    /// <paramref name="player"/>'s inventory on the tile they stand on, or the whole stack. It
    /// belongs to them for a while.
    /// </summary>
    /// <returns><see cref="Outcome.NothingThere"/> for anything that stopped it, as the caller's cue to tell the player.</returns>
    public Outcome DropFrom(string player, int slot, int? count = null)
    {
        if (_options.TileOf(player) is not { } tile || _options.InventoryOf(player)?.Take(slot, count) is not { } taken)
        {
            return Outcome.NothingThere;
        }

        Drop(taken.Item, taken.Count, tile, player);
        return Outcome.Done;
    }

    /// <summary>
    /// Moves the item <paramref name="networkId"/> into <paramref name="player"/>'s inventory. The
    /// player must stand on its tile and be allowed to see it. A bag with room for only part takes
    /// that part and leaves the rest lying there.
    /// </summary>
    /// <returns>The outcome, as the caller's cue to tell the player.</returns>
    public Outcome PickUp(string player, ulong networkId)
    {
        var inventory = _options.InventoryOf(player);
        if (!_lying.TryGetValue(networkId, out var item) || inventory is null || !CanSee(player, networkId))
        {
            return Outcome.NothingThere;
        }

        if (_options.TileOf(player) != item.Tile)
        {
            return Outcome.NotAllowed;
        }

        var added = inventory.Add(item.Item, item.Count);
        if (added == 0)
        {
            return Outcome.WontFit;
        }

        if (added == item.Count)
        {
            Remove(item);
            return Outcome.Done;
        }

        // Less than the count was added, so the rest still fits in an int.
        item.Count -= (int)added;
        WriteGround(item.Entity, 1, item.Count);
        return Outcome.Partly;
    }

    private void Expire()
    {
        var now = _registry.Actions.Tick;
        foreach (var item in _lying.Values.ToList())
        {
            if (now >= item.OwnedUntil + PublicTicks)
            {
                Remove(item);
            }
            else if (now >= item.OwnedUntil)
            {
                item.Owner = null;
            }
        }
    }

    private void Remove(Dropped item)
    {
        _lying.Remove(item.NetworkId);
        _services.Hosting.Despawn(item.Entity);
    }

    private void WriteGround(ulong entity, int index, int value)
    {
        if (_services.World.TryGetBytes(entity, _ground, out var bytes))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * sizeof(int))..], value);
        }
    }

    private sealed class Dropped
    {
        public required ulong Entity { get; init; }

        public required ulong NetworkId { get; init; }

        public required int Item { get; init; }

        public required TileCoord Tile { get; init; }

        public required long OwnedUntil { get; init; }

        public required int Count { get; set; }

        public required string? Owner { get; set; }
    }
}
