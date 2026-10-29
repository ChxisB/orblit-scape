using System.Buffers.Binary;

namespace Scape.Core.Tests;

public class ActorTests
{
    private static int[] NoneWorn() => new int[ItemDef.EquipSlots.Count];

    private static Look ALook(int skin = 0xE0AC69, IReadOnlyList<int>? worn = null) =>
        new(skin, 0x3B2F2F, 0x2F6FDB, worn ?? NoneWorn());

    // An Anim.

    [Fact]
    public void AnAnimKeepsItsStateThroughTheBytes()
    {
        var anim = new Anim(ActorState.Run, ActorState.Hurt, 7);
        var bytes = new byte[Anim.Ints * 4];

        Anim.Write(bytes, anim);

        Assert.Equal(anim, Anim.TryRead(bytes));
    }

    [Fact]
    public void AnAnimHasNoActUntilOnePlays()
    {
        var bytes = new byte[Anim.Ints * 4];
        Anim.Write(bytes, new Anim());

        Assert.Null(Anim.TryRead(bytes)!.Act);
    }

    [Fact]
    public void AnAnimCountsEachActAndTheSameActTwiceIsAChange()
    {
        var once = new Anim().Playing(ActorState.Attack);
        var twice = once.Playing(ActorState.Attack);

        Assert.NotEqual(once, twice);
        Assert.Equal(2, twice.Seq);
    }

    [Fact]
    public void AnAnimWrapsItsCountBackToZero()
    {
        var last = new Anim(act: ActorState.Attack, seq: Anim.MaxSeq);

        Assert.Equal(0, last.Playing(ActorState.Attack).Seq);
    }

    [Fact]
    public void AnAnimKeepsTheActWhenTheRestingStateChanges()
    {
        var anim = new Anim().Playing(ActorState.Gather).WithBase(ActorState.Walk);

        Assert.Equal((ActorState.Walk, (ActorState?)ActorState.Gather, 1), (anim.Base, anim.Act, anim.Seq));
    }

    [Fact]
    public void AnAnimRefusesAnActAsTheRestingStateAndTheReverse()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Anim(ActorState.Attack));
        Assert.ThrowsAny<ArgumentException>(() => new Anim(act: ActorState.Walk));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anim(seq: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Anim(seq: Anim.MaxSeq + 1));
    }

    [Theory]
    [InlineData(9, -1, 0)]
    [InlineData(-1, -1, 0)]
    [InlineData(0, 2, 0)]
    [InlineData(0, 8, 0)]
    [InlineData(0, -2, 0)]
    [InlineData(0, -1, -1)]
    public void AnAnimReadsBytesThatNameNoStateAsNull(int @base, int act, int seq)
    {
        var bytes = new byte[Anim.Ints * 4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, @base);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), act);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), seq);

        Assert.Null(Anim.TryRead(bytes));
    }

    // A Look.

    [Fact]
    public void ALookKeepsItsColoursAndWornItemsThroughTheBytes()
    {
        int[] worn = [.. Enumerable.Range(0, ItemDef.EquipSlots.Count).Select(slot => slot * 3)];
        var look = ALook(worn: worn);
        var bytes = new byte[Look.Ints * 4];

        Look.Write(bytes, look);

        Assert.Equal(look, Look.TryRead(bytes));
        Assert.Equal(worn, Look.TryRead(bytes)!.Worn);
    }

    [Fact]
    public void ALookIsNotTheSameLookWhenOneSlotDiffers()
    {
        var worn = NoneWorn();
        worn[3] = 12;

        Assert.NotEqual(ALook(), ALook(worn: worn));
        Assert.Equal(ALook(worn: worn), ALook().WithWorn(worn));
    }

    [Fact]
    public void ALookCannotBeChangedThroughTheListItWasMadeWith()
    {
        var worn = NoneWorn();
        var look = ALook(worn: worn);

        worn[0] = 5;

        Assert.Equal(0, look.Worn[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)look.Worn)[0] = 5);
    }

    [Fact]
    public void ALookRefusesAColourAnItemOrACountOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ALook(skin: 0x1000000));
        Assert.Throws<ArgumentOutOfRangeException>(() => ALook(skin: -1));
        Assert.ThrowsAny<ArgumentException>(() => ALook(worn: [1, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ALook(worn: [.. Enumerable.Repeat(ItemDef.MaxId + 1, ItemDef.EquipSlots.Count)]));
    }

    [Fact]
    public void ALookReadsBytesThatAreNotALookAsNull()
    {
        var bytes = new byte[Look.Ints * 4];
        Look.Write(bytes, ALook());
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), -4);

        Assert.Null(Look.TryRead(bytes));
    }
}
