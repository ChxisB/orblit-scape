namespace Scape.Content.Tests;

public class NodesTests
{
    [Fact]
    public void EachGatherableObjectHasANodeAndEachNodeADifferentOne()
    {
        int[] objects = [ObjectId.Tree, ObjectId.Rock, ObjectId.FishingSpot];

        Assert.Equal(objects.Length, DefaultNodes.All.Nodes.Count);
        Assert.All(objects, id => Assert.NotNull(DefaultNodes.All.Find(id)));
    }

    [Fact]
    public void ANodeNeedsAToolTheFirstGameHasAndGivesAnItemItHas()
    {
        var tree = DefaultNodes.All.Find(ObjectId.Tree)!;
        var rock = DefaultNodes.All.Find(ObjectId.Rock)!;
        var spot = DefaultNodes.All.Find(ObjectId.FishingSpot)!;

        Assert.Equal((ItemId.BronzeAxe, ItemId.Logs), (tree.Tool, tree.Product));
        Assert.Equal((ItemId.BronzePickaxe, ItemId.CopperOre), (rock.Tool, rock.Product));
        Assert.Equal((ItemId.FishingNet, ItemId.RawFish), (spot.Tool, spot.Product));
    }

    [Fact]
    public void EveryNodeIsGatherableAtLevel1AndGetsEasierByLevel30()
    {
        foreach (var node in DefaultNodes.All.Nodes)
        {
            Assert.Equal(1, node.Level);
            Assert.True(node.ChanceAt(30) > node.ChanceAt(1));
        }
    }
}
