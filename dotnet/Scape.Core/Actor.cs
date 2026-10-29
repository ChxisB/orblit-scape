using System.Buffers.Binary;

namespace Scape.Core;

/// <summary>
/// What an actor is doing. The first three are how it stands between other
/// things, and the server keeps them true. The rest play once, when an event
/// starts them.
/// </summary>
public enum ActorState
{
    Idle,
    Walk,
    Run,
    Attack,
    Hurt,
    Death,
    Gather,
    Make,
}

public static class ActorStates
{
    extension(ActorState state)
    {
        /// <summary>Whether this state plays once, started by an event.</summary>
        public bool IsAct => state > ActorState.Run;
    }
}

/// <summary>
/// What an actor is doing: how it rests, and the act it last played. The
/// count of acts goes up with each one, so the same act twice in a row is
/// still a change a client can see.
/// </summary>
public sealed record Anim
{
    /// <summary>The replicated component holding what an actor is doing.</summary>
    public const string Name = "Anim";

    /// <summary>Ints in the component: the resting state, the act or -1, then the count of acts so far.</summary>
    public const int Ints = 3;

    /// <summary>The count of acts wraps back to zero after this.</summary>
    public const int MaxSeq = 0xFFFF;

    public Anim(ActorState @base = ActorState.Idle, ActorState? act = null, int seq = 0)
    {
        if (@base.IsAct)
        {
            throw new ArgumentException($"{@base} is not a resting state.", nameof(@base));
        }

        if (act is { } played && !played.IsAct)
        {
            throw new ArgumentException($"{played} is a resting state.", nameof(act));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(seq);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seq, MaxSeq);
        Base = @base;
        Act = act;
        Seq = seq;
    }

    public ActorState Base { get; }

    public ActorState? Act { get; }

    public int Seq { get; }

    public Anim WithBase(ActorState next) => new(next, Act, Seq);

    /// <summary>The same actor playing <paramref name="next"/> now.</summary>
    public Anim Playing(ActorState next) => new(Base, next, Seq >= MaxSeq ? 0 : Seq + 1);

    /// <summary>Writes <paramref name="anim"/> into the component's bytes.</summary>
    public static void Write(Span<byte> bytes, Anim anim)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)anim.Base);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], anim.Act is { } act ? (int)act : -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], anim.Seq);
    }

    /// <summary>Reads the component's bytes, or null for bytes that name no state.</summary>
    public static Anim? TryRead(ReadOnlySpan<byte> bytes)
    {
        var @base = StateAt(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        var actIndex = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var act = actIndex == -1 ? null : StateAt(actIndex);
        if (@base is null || (actIndex != -1 && act is null))
        {
            return null;
        }

        try
        {
            return new Anim(@base.Value, act, BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ActorState? StateAt(int index) =>
        index >= 0 && index <= (int)ActorState.Make ? (ActorState)index : null;
}

/// <summary>
/// How a player looks: three colours and what each equipment slot shows. The
/// server sends item ids and the client builds the parts, so a new part is
/// content and not a new message.
/// </summary>
public sealed class Look : IEquatable<Look>
{
    /// <summary>The replicated component holding how a player looks.</summary>
    public const string Name = "Look";

    /// <summary>The colours of a look: skin, hair and cloth.</summary>
    public const int Colours = 3;

    private const int MaxColour = 0xFFFFFF;

    /// <summary>
    /// Ints in the component: the colours, then the item worn in each of the
    /// <see cref="ItemDef.EquipSlots"/>, 0 for a slot that is empty.
    /// </summary>
    public static readonly int Ints = Colours + ItemDef.EquipSlots.Count;

    public Look(int skin, int hair, int cloth, IReadOnlyList<int> worn)
    {
        foreach (var colour in (int[])[skin, hair, cloth])
        {
            ArgumentOutOfRangeException.ThrowIfNegative(colour, nameof(colour));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(colour, MaxColour, nameof(colour));
        }

        if (worn.Count != ItemDef.EquipSlots.Count)
        {
            throw new ArgumentException("The worn items are not one item per slot.", nameof(worn));
        }

        foreach (var item in worn)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(item, nameof(worn));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(item, ItemDef.MaxId, nameof(worn));
        }

        Skin = skin;
        Hair = hair;
        Cloth = cloth;
        Worn = [.. worn];
    }

    /// <summary>A colour as 0xRRGGBB.</summary>
    public int Skin { get; }

    public int Hair { get; }

    public int Cloth { get; }

    /// <summary>The item id worn in each slot, in the order of <see cref="ItemDef.EquipSlots"/>.</summary>
    public IReadOnlyList<int> Worn { get; }

    public Look WithWorn(IReadOnlyList<int> next) => new(Skin, Hair, Cloth, next);

    /// <summary>Writes <paramref name="look"/> into the component's bytes.</summary>
    public static void Write(Span<byte> bytes, Look look)
    {
        BinaryPrimitives.WriteInt32LittleEndian(bytes, look.Skin);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], look.Hair);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], look.Cloth);
        for (var slot = 0; slot < look.Worn.Count; slot++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[((Colours + slot) * 4)..], look.Worn[slot]);
        }
    }

    /// <summary>Reads the component's bytes, or null for bytes that are not a look.</summary>
    public static Look? TryRead(ReadOnlySpan<byte> bytes)
    {
        var ints = new int[Ints];
        for (var i = 0; i < Ints; i++)
        {
            ints[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(i * 4)..]);
        }

        try
        {
            return new Look(ints[0], ints[1], ints[2], ints[Colours..]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool Equals(Look? other) =>
        other is not null
        && other.Skin == Skin
        && other.Hair == Hair
        && other.Cloth == Cloth
        && other.Worn.SequenceEqual(Worn);

    public override bool Equals(object? obj) => Equals(obj as Look);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Skin);
        hash.Add(Hair);
        hash.Add(Cloth);
        foreach (var item in Worn)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
