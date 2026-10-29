namespace Scape.Content.Tests;

public class ObjectsTests
{
    [Fact]
    public void ADoorIsSolidAndOffersOpenWhenShutAndCloseWhenOpen()
    {
        var door = DefaultObjects.All.Require(ObjectId.Door);

        Assert.True(door.Solid);
        Assert.Equal(["Open"], door.Options.Where(o => o.IsOfferedIn(0)).Select(o => o.Label));
        Assert.Equal(["Close"], door.Options.Where(o => o.IsOfferedIn(1)).Select(o => o.Label));
    }

    [Fact]
    public void StairsAreNotSolidAndEachHasOneWayToClimb()
    {
        foreach (var id in new[] { ObjectId.StairsUp, ObjectId.StairsDown })
        {
            var stairs = DefaultObjects.All.Require(id);

            Assert.False(stairs.Solid);
            Assert.Equal(["climb"], stairs.Options.Select(o => o.Action));
        }
    }

    [Fact]
    public void ATreeARockAndAFishingSpotAreGatheredOnlyWhileFull()
    {
        foreach (var id in new[] { ObjectId.Tree, ObjectId.Rock, ObjectId.FishingSpot })
        {
            var node = DefaultObjects.All.Require(id);

            Assert.Equal(["gather"], node.Options.Select(o => o.Action));
            Assert.True(node.Options.Single().IsOfferedIn(0));
            Assert.False(node.Options.Single().IsOfferedIn(1));
        }
    }

    [Fact]
    public void ABankBoothIsSolidAndOffersOneWayToBank()
    {
        var booth = DefaultObjects.All.Require(ObjectId.BankBooth);

        Assert.True(booth.Solid);
        Assert.Equal(["bank"], booth.Options.Select(o => o.Action));
    }
}
