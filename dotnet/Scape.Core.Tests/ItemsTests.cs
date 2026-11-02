using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class ItemsTests
{
    // ItemDef.

    [Fact]
    public void RejectsAnIdOutside1ToMaxId()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(0, "x"));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(ItemDef.MaxId + 1, "x"));
        Assert.Equal(ItemDef.MaxId, new ItemDef(ItemDef.MaxId, "x").Id);
    }

    [Fact]
    public void RejectsAnEmptyNameAndANegativeValue()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, ""));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "x", value: -1));
    }

    [Fact]
    public void RejectsASlotThatIsNotWornAndALevelOutOfRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "x", equipSlot: "tail"));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "x", needs: new Needs("a", 0)));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "x", needs: new Needs("a", SkillDef.MaxLevel + 1)));
    }

    // ItemSet.

    [Fact]
    public void FindsAnItemAndRequiresOne()
    {
        var items = Items();

        Assert.Equal("Sword", items.Find(Sword)?.Name);
        Assert.Null(items.Find(99));
        Assert.True(items.Require(Coins).Stackable);
        Assert.ThrowsAny<ArgumentException>(() => items.Require(99));
    }

    [Fact]
    public void RefusesARepeatedId()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemSet([new ItemDef(1, "a"), new ItemDef(1, "b")], Skills));
    }

    [Fact]
    public void RefusesARequirementOnASkillTheGameDoesNotHave()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemSet([new ItemDef(1, "a", needs: new Needs("baking", 2))], Skills));
    }

    // ItemStack.

    [Fact]
    public void RejectsAnItemOrACountOutsideRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemStack(0, 1));
        Assert.ThrowsAny<ArgumentException>(() => new ItemStack(ItemDef.MaxId + 1, 1));
        Assert.ThrowsAny<ArgumentException>(() => new ItemStack(1, 0));

        // An int cannot hold MaxCount + 1, so there is no check above the cap to test.
        Assert.Equal(ItemStack.MaxCount, new ItemStack(1, ItemStack.MaxCount).Count);
    }

    [Fact]
    public void ComparesByValue()
    {
        Assert.Equal(new ItemStack(1, 2), new ItemStack(1, 2));
        Assert.Equal(new ItemStack(1, 2).GetHashCode(), new ItemStack(1, 2).GetHashCode());
        Assert.NotEqual(new ItemStack(1, 2), new ItemStack(1, 3));
        Assert.NotEqual(new ItemStack(1, 2), new ItemStack(2, 2));
    }
}
