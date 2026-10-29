using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Scape.Core;

namespace Scape.Content;

/// <summary>
/// The part of an actor that a box moves with. A pose turns a bone about the
/// pivot its model gives it.
/// </summary>
public enum ModelBone
{
    Torso,
    Head,
    ArmL,
    ArmR,
    LegL,
    LegR,
}

/// <summary>
/// A point or a size in tiles, with y up and the actor facing +z. The actor
/// stands with its feet at the origin.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    internal Vec3 Scaled(double k) => new(X * k, Y * k, Z * k);
}

/// <summary>
/// One box of a model: it moves with <see cref="Bone"/>, sits with its middle
/// at <see cref="At"/>, and is <see cref="Colour"/> as 0xRRGGBB.
/// </summary>
public sealed record ModelBox(ModelBone Bone, Vec3 At, Vec3 Size, int Colour);

/// <summary>
/// A shape built in code. The version in <see cref="Name"/> goes up when the
/// shape changes, so a client that cached the old one knows to draw it again.
/// A person's name also holds their look, so two looks never share a name.
/// <see cref="Pivots"/> says where each bone turns: the hip, the neck and the
/// shoulders of a person.
/// </summary>
public sealed class ActorModel(string name, IReadOnlyList<ModelBox> boxes, IReadOnlyDictionary<ModelBone, Vec3> pivots)
{
    public string Name { get; } = name;

    public IReadOnlyList<ModelBox> Boxes { get; } = [.. boxes];

    public IReadOnlyDictionary<ModelBone, Vec3> Pivots { get; } = pivots.ToDictionary().AsReadOnly();
}

/// <summary>
/// The models of a game: what each creature is drawn as, and what a player is
/// drawn as given their look.
/// </summary>
public sealed class ModelSet(IReadOnlyDictionary<int, ActorModel> npcs)
{
    private const string PersonName = "person.v1";

    private readonly Dictionary<int, ActorModel> _npcs = npcs.ToDictionary();

    /// <summary>The ids of the creatures that have a model.</summary>
    public IEnumerable<int> NpcIds => _npcs.Keys;

    /// <summary>The model of the creature <paramref name="id"/>, or null for one with no model.</summary>
    public ActorModel? Npc(int id) => _npcs.GetValueOrDefault(id);

    /// <summary>
    /// The person <paramref name="look"/> describes: the body in their colours,
    /// then the part each worn item shows.
    /// </summary>
    [SuppressMessage("Performance", "CA1822", Justification = "Callers ask the set, so it stays an instance method.")]
    public ActorModel Player(Look look) => new(
        NameOf(look),
        [
            .. Bodies.Person(look.Skin, look.Hair, look.Cloth),
            .. Enumerable.Range(0, look.Worn.Count)
                .Where(slot => look.Worn[slot] != 0)
                .SelectMany(slot => Bodies.Worn(ItemDef.EquipSlots[slot], look.Worn[slot])),
        ],
        Bodies.PersonPivots);

    private static string NameOf(Look look)
    {
        int[] parts = [look.Skin, look.Hair, look.Cloth, .. look.Worn];
        return string.Join('.', [PersonName, .. parts.Select(p => p.ToString("x", CultureInfo.InvariantCulture))]);
    }
}

/// <summary>The boxes of a person, shared by players and the creatures shaped like one.</summary>
internal static class Bodies
{
    public static readonly IReadOnlyDictionary<ModelBone, Vec3> PersonPivots = new Dictionary<ModelBone, Vec3>
    {
        [ModelBone.Torso] = new(0, 0.7, 0),
        [ModelBone.Head] = new(0, 1.35, 0),
        [ModelBone.ArmL] = new(-0.32, 1.35, 0),
        [ModelBone.ArmR] = new(0.32, 1.35, 0),
        [ModelBone.LegL] = new(-0.14, 0.7, 0),
        [ModelBone.LegR] = new(0.14, 0.7, 0),
    }.AsReadOnly();

    private static readonly int[] ItemColours =
    [
        0xB87333,
        0x9EA3A8,
        0xD4AF37,
        0x5B6770,
        0x7A2E2E,
        0x2E6F40,
        0x4B3F72,
        0xC8C2B0,
    ];

    /// <summary>What a slot shows when something is worn in it. The ring shows nothing.</summary>
    private static readonly Dictionary<string, Part[]> SlotParts = new()
    {
        ["head"] = [new(ModelBone.Head, new(0, 1.64, 0), new(0.35, 0.16, 0.35))],
        ["neck"] = [new(ModelBone.Head, new(0, 1.36, 0), new(0.36, 0.06, 0.2))],
        ["cape"] = [new(ModelBone.Torso, new(0, 1.0, -0.17), new(0.46, 0.8, 0.06))],
        ["weapon"] = [new(ModelBone.ArmR, new(0.32, 0.55, 0.18), new(0.06, 0.7, 0.06))],
        ["body"] = [new(ModelBone.Torso, new(0, 1.07, 0), new(0.54, 0.5, 0.32))],
        ["shield"] = [new(ModelBone.ArmL, new(-0.4, 1.0, 0), new(0.05, 0.4, 0.34))],
        ["legs"] =
        [
            new(ModelBone.LegL, new(-0.14, 0.45, 0), new(0.22, 0.5, 0.24)),
            new(ModelBone.LegR, new(0.14, 0.45, 0), new(0.22, 0.5, 0.24)),
        ],
        ["hands"] =
        [
            new(ModelBone.ArmL, new(-0.32, 0.78, 0), new(0.16, 0.14, 0.18)),
            new(ModelBone.ArmR, new(0.32, 0.78, 0), new(0.16, 0.14, 0.18)),
        ],
        ["feet"] =
        [
            new(ModelBone.LegL, new(-0.14, 0.06, 0.04), new(0.22, 0.12, 0.3)),
            new(ModelBone.LegR, new(0.14, 0.06, 0.04), new(0.22, 0.12, 0.3)),
        ],
        ["ammo"] = [new(ModelBone.Torso, new(-0.1, 1.1, -0.2), new(0.1, 0.4, 0.1))],
    };

    public static IReadOnlyDictionary<ModelBone, Vec3> PivotsAt(double scale) =>
        PersonPivots.ToDictionary(p => p.Key, p => p.Value.Scaled(scale)).AsReadOnly();

    public static IReadOnlyList<ModelBox> Person(int skin, int hair, int cloth, double scale = 1)
    {
        var legs = Darker(cloth);
        ModelBox[] boxes =
        [
            new(ModelBone.LegL, new(-0.14, 0.35, 0), new(0.2, 0.7, 0.22), legs),
            new(ModelBone.LegR, new(0.14, 0.35, 0), new(0.2, 0.7, 0.22), legs),
            new(ModelBone.Torso, new(0, 1.025, 0), new(0.5, 0.65, 0.28), cloth),
            new(ModelBone.ArmL, new(-0.32, 1.05, 0), new(0.14, 0.6, 0.16), cloth),
            new(ModelBone.ArmR, new(0.32, 1.05, 0), new(0.14, 0.6, 0.16), cloth),
            new(ModelBone.Head, new(0, 1.52, 0), new(0.3, 0.3, 0.3), skin),
            new(ModelBone.Head, new(0, 1.69, 0), new(0.32, 0.08, 0.32), hair),
        ];
        return [.. boxes.Select(box => box with { At = box.At.Scaled(scale), Size = box.Size.Scaled(scale) })];
    }

    public static IReadOnlyList<ModelBox> Worn(string slot, int item)
    {
        var colour = ItemColours[item % ItemColours.Length];
        return [.. SlotParts.GetValueOrDefault(slot, []).Select(p => new ModelBox(p.Bone, p.At, p.Size, colour))];
    }

    private static int Darker(int colour) =>
        ((((colour >> 16) & 0xFF) * 7 / 10) << 16) | ((((colour >> 8) & 0xFF) * 7 / 10) << 8) | ((colour & 0xFF) * 7 / 10);

    private readonly record struct Part(ModelBone Bone, Vec3 At, Vec3 Size);
}

public static class DefaultModels
{
    // Above All, since static fields start in the order they are written.
    private static readonly IReadOnlyDictionary<ModelBone, Vec3> ChickenPivots = new Dictionary<ModelBone, Vec3>
    {
        [ModelBone.Torso] = new(0, 0.2, 0),
        [ModelBone.Head] = new(0, 0.46, 0.14),
        [ModelBone.ArmL] = new(-0.13, 0.44, 0),
        [ModelBone.ArmR] = new(0.13, 0.44, 0),
        [ModelBone.LegL] = new(-0.06, 0.2, 0),
        [ModelBone.LegR] = new(0.06, 0.2, 0),
    }.AsReadOnly();

    /// <summary>The models the game ships with. Real ones arrive through packs.</summary>
    public static ModelSet All { get; } = new(new Dictionary<int, ActorModel>
    {
        [NpcId.Chicken] = new("chicken.v1", Chicken(), ChickenPivots),
        [NpcId.Goblin] = PersonModel("goblin.v1", skin: 0x5E8C3A, hair: 0x2B3A1A, cloth: 0x7A5230, scale: 0.8),
        [NpcId.Guard] = PersonModel(
            "guard.v1",
            skin: 0xD9A877,
            hair: 0x4A3B2A,
            cloth: 0x3E5C8A,
            extra: [.. Bodies.Worn("head", 1), .. Bodies.Worn("weapon", 1)]),
        [NpcId.Shopkeeper] = PersonModel("shopkeeper.v1", skin: 0xC68E5F, hair: 0x8A8A8A, cloth: 0x8A3E3E),
        [NpcId.Lamplighter] = PersonModel("lamplighter.v1", skin: 0xE0B48A, hair: 0x5A4632, cloth: 0xB89B3E),
    });

    // F2: a row of content names its fields, and most take their defaults.
    private static ActorModel PersonModel(
        string name,
        int skin,
        int hair,
        int cloth,
        double scale = 1,
        IReadOnlyList<ModelBox>? extra = null) =>
        new(name, [.. Bodies.Person(skin, hair, cloth, scale), .. extra ?? []], Bodies.PivotsAt(scale));

    private static ModelBox[] Chicken() =>
    [
        new(ModelBone.LegL, new(-0.06, 0.1, 0), new(0.03, 0.2, 0.03), 0xE8A33D),
        new(ModelBone.LegR, new(0.06, 0.1, 0), new(0.03, 0.2, 0.03), 0xE8A33D),
        new(ModelBone.Torso, new(0, 0.33, 0), new(0.26, 0.26, 0.38), 0xF2F2EC),
        new(ModelBone.ArmL, new(-0.15, 0.36, 0), new(0.04, 0.16, 0.26), 0xE6E6DE),
        new(ModelBone.ArmR, new(0.15, 0.36, 0), new(0.04, 0.16, 0.26), 0xE6E6DE),
        new(ModelBone.Head, new(0, 0.55, 0.2), new(0.14, 0.16, 0.14), 0xF2F2EC),
        new(ModelBone.Head, new(0, 0.53, 0.3), new(0.05, 0.04, 0.08), 0xE8A33D),
        new(ModelBone.Head, new(0, 0.65, 0.2), new(0.04, 0.05, 0.1), 0xC0392B),
    ];
}
