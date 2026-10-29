namespace Scape.Core;

/// <summary>The replicated component on an object standing in the world.</summary>
public static class WorldObject
{
    public const string Name = "WorldObject";

    /// <summary>Ints in the component: the object id, then its state.</summary>
    public const int Ints = 2;
}

/// <summary>What the first byte of an interact message names. The id and option that follow mean what each kind says.</summary>
public static class InteractKind
{
    /// <summary>A placed object. The id is its network id, the option an index into its options.</summary>
    public const int PlacedObject = 0;

    /// <summary>An item lying on the ground. The id is its network id. The option is unused.</summary>
    public const int Ground = 1;

    /// <summary>An inventory item used on an object. The id is the object's network id, the option the inventory slot.</summary>
    public const int UseOnObject = 2;

    /// <summary>An inventory item used on another. The id is the slot of the item used on, the option the slot of the item used.</summary>
    public const int UseOnItem = 3;

    /// <summary>A creature. The id is its network id. The option is unused, and the click is an attack.</summary>
    public const int Npc = 4;

    /// <summary>Another player. The id is their network id. The option is unused, and the click asks to trade.</summary>
    public const int Player = 5;
}

/// <summary>
/// One entry in the menu of an object. <see cref="Action"/> names what a feature runs. With
/// <see cref="WhenState"/> set the option is offered only while the object is in that state,
/// so a door lists Open when shut and Close when open.
/// </summary>
public sealed class ObjectOption
{
    public ObjectOption(string label, string action, int? whenState = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentException.ThrowIfNullOrEmpty(action);
        Label = label;
        Action = action;
        WhenState = whenState;
    }

    public string Label { get; }

    public string Action { get; }

    public int? WhenState { get; }

    public bool IsOfferedIn(int state) => WhenState is null || WhenState == state;
}

/// <summary>One kind of object: a row of content.</summary>
/// <remarks>
/// State 0 is the whole object. Any other state is a changed one, such as an open door or a
/// felled tree. A <see cref="Solid"/> object blocks its tiles in state 0 only.
/// </remarks>
public sealed class ObjectDef
{
    /// <summary>The highest object id.</summary>
    public const int MaxId = 0xFFFF;

    // F2: a row of content names its fields, and most take their defaults.
    public ObjectDef(
        int id,
        string name,
        int width = 1,
        int height = 1,
        bool solid = false,
        IReadOnlyList<ObjectOption>? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (width < 1 || height < 1)
        {
            throw new ArgumentException("An object is at least one tile each way.");
        }

        Options = [.. options ?? []];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Options.Count, 0xFF, nameof(options));
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Solid = solid;
    }

    public int Id { get; }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    public bool Solid { get; }

    public IReadOnlyList<ObjectOption> Options { get; }

    /// <summary>Whether the object blocks the tiles it stands on while in <paramref name="state"/>.</summary>
    public bool BlocksIn(int state) => Solid && state == 0;

    /// <summary>The tiles this object covers when its south-west tile is <paramref name="origin"/>.</summary>
    public Footprint FootprintAt(TileCoord origin) => new(origin, Width, Height);
}

/// <summary>The objects a game has.</summary>
public sealed class ObjectSet
{
    private readonly Dictionary<int, ObjectDef> _byId;

    public ObjectSet(IReadOnlyList<ObjectDef> objects)
    {
        Objects = [.. objects];
        _byId = Objects.DistinctBy(o => o.Id).ToDictionary(o => o.Id);
        if (_byId.Count != Objects.Count)
        {
            throw new ArgumentException("The objects repeat an id.", nameof(objects));
        }
    }

    public IReadOnlyList<ObjectDef> Objects { get; }

    public ObjectDef? Find(int id) => _byId.GetValueOrDefault(id);

    /// <summary>The object called <paramref name="id"/>. Throws <see cref="ArgumentException"/> when there is none.</summary>
    public ObjectDef Require(int id) =>
        Find(id) ?? throw new ArgumentException(FormattableString.Invariant($"{id} is not an object."), nameof(id));
}

/// <summary>A place an object is put.</summary>
/// <param name="ObjectId">The id of the <see cref="ObjectDef"/>.</param>
/// <param name="Tile">The south-west tile of its footprint.</param>
/// <param name="Destination">Where a player who climbs it is set down, or null for an object that cannot be climbed.</param>
public readonly record struct ObjectPlacement(int ObjectId, TileCoord Tile, TileCoord? Destination = null)
{
    public override string ToString() => $"ObjectPlacement({ObjectId} at {Tile})";
}
