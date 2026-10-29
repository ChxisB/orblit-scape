namespace Scape.Core.Tests;

public class NpcsTests
{
    private static readonly NpcSet Set = new([new NpcDef(1, "Guard")]);

    // Aggressive.

    [Fact]
    public void NeedsASightOfAtLeastOneTile()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Aggressive(0, 5));
    }

    [Fact]
    public void NeedsALeashNoShorterThanItsSight()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Aggressive(5, 4));
        Assert.Equal(5, new Aggressive(5, 5).Leash);
    }

    // NpcDef.

    [Fact]
    public void StandsStillUnlessToldOtherwise()
    {
        Assert.IsType<Idle>(new NpcDef(1, "Guard").Behaviour);
    }

    [Fact]
    public void RejectsAnIdOutsideRangeAndAnEmptyName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new NpcDef(0, "A"));
        Assert.ThrowsAny<ArgumentException>(() => new NpcDef(NpcDef.MaxId + 1, "A"));
        Assert.Equal(NpcDef.MaxId, new NpcDef(NpcDef.MaxId, "A").Id);
        Assert.ThrowsAny<ArgumentException>(() => new NpcDef(1, ""));
    }

    [Fact]
    public void NamesAConversationOrNoneNeverAnEmptyKey()
    {
        Assert.Null(new NpcDef(1, "A").Dialogue);
        Assert.Equal("a", new NpcDef(1, "A", dialogue: "a").Dialogue);
        Assert.ThrowsAny<ArgumentException>(() => new NpcDef(1, "A", dialogue: ""));
    }

    // NpcSet.

    [Fact]
    public void FindsACreatureById()
    {
        Assert.Equal("Guard", Set.Find(1)!.Name);
        Assert.Null(Set.Find(2));
    }

    [Fact]
    public void RequireThrowsForACreatureThatDoesNotExist()
    {
        Assert.Equal(1, Set.Require(1).Id);
        Assert.ThrowsAny<ArgumentException>(() => Set.Require(2));
    }

    [Fact]
    public void RejectsARepeatedId()
    {
        Assert.ThrowsAny<ArgumentException>(() => new NpcSet([new NpcDef(1, "A"), new NpcDef(1, "B")]));
    }

    // NpcSpawn.

    [Fact]
    public void HasNoRadiusByDefaultAndRejectsANegativeOne()
    {
        var tile = new TileCoord(1, 1, 0);

        Assert.Equal(0, new NpcSpawn(1, tile).Radius);
        Assert.ThrowsAny<ArgumentException>(() => new NpcSpawn(1, tile, -1));
    }
}
