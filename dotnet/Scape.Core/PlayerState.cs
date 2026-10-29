using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Orblit.Json;

namespace Scape.Core;

/// <summary>
/// Everything a server keeps about a player between visits: where they stand, their experience,
/// what they carry, wear and bank, and their lists.
/// </summary>
/// <remarks>
/// It is a value. <see cref="Encode"/> and <see cref="Decode"/> are the only way it becomes bytes,
/// and the bytes are JSON, so an operator can read a save and fix one. Skills are named by id and
/// items by number, so a game that adds a skill leaves old saves valid.
/// </remarks>
public sealed class PlayerState
{
    /// <summary>The version <see cref="Encode"/> writes. A reader refuses a later one.</summary>
    public const int Version = 2;

    /// <summary>The most experience a skill may hold in a save. A progress table keeps it in 32 bits.</summary>
    public const long MaxSavedXp = 0xFFFFFFFF;

    // F2: six parameters, all named at each call.
    public PlayerState(
        TileCoord tile,
        IReadOnlyDictionary<string, long>? skills = null,
        IReadOnlyDictionary<string, long>? quests = null,
        IReadOnlyDictionary<ContainerId, IReadOnlyList<ItemStack?>>? containers = null,
        IEnumerable<string>? friends = null,
        IEnumerable<string>? ignored = null)
    {
        Tile = tile;
        Skills = Wholes(skills, nameof(skills));
        Quests = Wholes(quests, nameof(quests));
        Containers = Slots(containers);
        Friends = Names(friends, nameof(friends));
        Ignored = Names(ignored, nameof(ignored));
    }

    public TileCoord Tile { get; }

    /// <summary>Experience by skill id. A skill that is absent has none.</summary>
    public IReadOnlyDictionary<string, long> Skills { get; }

    /// <summary>The stage of each quest by quest key. A quest that is absent is not begun.</summary>
    public IReadOnlyDictionary<string, long> Quests { get; }

    /// <summary>The slots of each container, empty ones null. A container that is absent is empty.</summary>
    public IReadOnlyDictionary<ContainerId, IReadOnlyList<ItemStack?>> Containers { get; }

    /// <summary>Player keys, lower case.</summary>
    public IReadOnlyList<string> Friends { get; }

    public IReadOnlyList<string> Ignored { get; }

    /// <summary>
    /// Reads what <see cref="Encode"/> wrote. Throws <see cref="FormatException"/> for bytes that
    /// are not a player state, or that a later version wrote.
    /// </summary>
    public static PlayerState Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return Read(JsonText.Decode(Wire.DecodeText(bytes)));
        }
        catch (ArgumentException e)
        {
            // The constructors check the values, so a bad value is a bad file. So are bytes that are not UTF-8.
            throw new FormatException("Not a player state: " + e.Message, e);
        }
    }

    /// <summary>The same bytes for the same state, whatever order it was built in.</summary>
    public byte[] Encode() => Encoding.UTF8.GetBytes(JsonText.Encode(new OrderedDictionary<string, object?>
    {
        ["version"] = Version,
        ["tile"] = new object?[] { Tile.X, Tile.Y, Tile.Plane },
        ["skills"] = Sorted(Skills),
        ["quests"] = Sorted(Quests),
        ["containers"] = SparseContainers(),
        ["friends"] = Friends,
        ["ignored"] = Ignored,
    }));

    private static string NameOf(ContainerId id) => JsonNamingPolicy.CamelCase.ConvertName(id.ToString());

    private static SortedDictionary<string, object?> Sorted(IReadOnlyDictionary<string, long> wholes) =>
        new(wholes.ToDictionary(pair => pair.Key, pair => (object?)pair.Value), StringComparer.Ordinal);

    private static List<object?> Sparse(IReadOnlyList<ItemStack?> slots)
    {
        var held = new List<object?>();
        for (var slot = 0; slot < slots.Count; slot++)
        {
            if (slots[slot] is { } stack)
            {
                held.Add(new object?[] { slot, stack.Item, stack.Count });
            }
        }

        return held;
    }

    private static ReadOnlyDictionary<string, long> Wholes(IReadOnlyDictionary<string, long>? wholes, string label)
    {
        var copy = new Dictionary<string, long>(wholes ?? new Dictionary<string, long>());
        foreach (var (key, value) in copy)
        {
            ArgumentException.ThrowIfNullOrEmpty(key, label);
            Wire.InRange(value, MaxSavedXp, key);
        }

        return copy.AsReadOnly();
    }

    // shortcut: a container whose size changes makes every older save fail to decode, since the
    // size is part of the check. Bump Version and read the old shape when a game first resizes one.
    private static ReadOnlyDictionary<ContainerId, IReadOnlyList<ItemStack?>> Slots(
        IReadOnlyDictionary<ContainerId, IReadOnlyList<ItemStack?>>? containers)
    {
        var copy = new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>();
        foreach (var (id, slots) in containers ?? new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>())
        {
            if (!Enum.IsDefined(id) || slots.Count != Container.SizeOf(id))
            {
                throw new ArgumentException(FormattableString.Invariant($"{id} is not a container of {slots.Count} slots."), nameof(containers));
            }

            copy[id] = Array.AsReadOnly([.. slots]);
        }

        return copy.AsReadOnly();
    }

    private static ReadOnlyCollection<string> Names(IEnumerable<string>? names, string label)
    {
        var copy = Array.AsReadOnly([.. names ?? []]);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(copy.Count, Wire.MaxListEntries, label);
        if (copy.FirstOrDefault(name => !Wire.IsValidName(name) || Wire.NameKey(name) != name) is { } bad)
        {
            throw new ArgumentException($"{bad} is not a player key.", label);
        }

        return copy;
    }

    private static FormatException Bad(string what) => new("Not a player state: " + what);

    private static PlayerState Read(object? json)
    {
        var map = AsObject(json, "the state");
        if (map.GetValueOrDefault("version") is not long version || version < 1)
        {
            throw Bad("no version");
        }

        if (version > Version)
        {
            throw Bad(FormattableString.Invariant($"version {version} is from a later server"));
        }

        if (map.GetValueOrDefault("tile") is List<object?> and [long x, long y, long plane])
        {
            return new PlayerState(
                new TileCoord(Wire.Narrow(x), Wire.Narrow(y), Wire.Narrow(plane)),
                skills: ReadWholes(map.GetValueOrDefault("skills"), "skill"),
                quests: ReadWholes(map.GetValueOrDefault("quests"), "quest"),
                containers: ReadContainers(map.GetValueOrDefault("containers")),
                friends: ReadNames(map.GetValueOrDefault("friends"), "friends"),
                ignored: ReadNames(map.GetValueOrDefault("ignored"), "ignored"));
        }

        throw Bad("tile is not [x, y, plane]");
    }

    private static OrderedDictionary<string, object?> AsObject(object? json, string what) =>
        json as OrderedDictionary<string, object?> ?? throw Bad($"{what} is not an object");

    private static List<object?> AsList(object? json, string what) =>
        json as List<object?> ?? throw Bad($"{what} is not a list");

    private static Dictionary<string, long>? ReadWholes(object? json, string what) =>
        json is null
            ? null
            : AsObject(json, what + "s").ToDictionary(
                pair => pair.Key,
                pair => pair.Value is long whole ? whole : throw Bad($"{what} {pair.Key} is not a number"));

    private static Dictionary<ContainerId, IReadOnlyList<ItemStack?>>? ReadContainers(object? json)
    {
        if (json is null)
        {
            return null;
        }

        var read = new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>();
        foreach (var (key, value) in AsObject(json, "containers"))
        {
            var id = Enum.GetValues<ContainerId>().Where(named => NameOf(named) == key).Cast<ContainerId?>().FirstOrDefault()
                ?? throw Bad($"no container called {key}");
            read[id] = ReadSlots(id, value);
        }

        return read;
    }

    private static ItemStack?[] ReadSlots(ContainerId id, object? json)
    {
        var slots = new ItemStack?[Container.SizeOf(id)];
        foreach (var entry in AsList(json, NameOf(id)))
        {
            if (entry is List<object?> and [long slot, long item, long count] && slot >= 0 && slot < slots.Length && slots[slot] is null)
            {
                slots[slot] = new ItemStack(Wire.Narrow(item), Wire.Narrow(count));
            }
            else
            {
                throw Bad($"{NameOf(id)} has a bad slot, {JsonText.Encode(entry)}");
            }
        }

        return slots;
    }

    private static List<string>? ReadNames(object? json, string what) =>
        json is null ? null : [.. AsList(json, what).Select(name => name as string ?? throw Bad($"{what} holds a non-name"))];

    private OrderedDictionary<string, object?> SparseContainers()
    {
        var sparse = new OrderedDictionary<string, object?>();
        foreach (var id in Enum.GetValues<ContainerId>())
        {
            if (Containers.TryGetValue(id, out var slots))
            {
                sparse[NameOf(id)] = Sparse(slots);
            }
        }

        return sparse;
    }
}
