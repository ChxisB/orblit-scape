using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class NodesTests
{
    private static readonly ObjectSet Objects = new([new ObjectDef(1, "Tree", solid: true), new ObjectDef(2, "Rock", solid: true)]);

    // F2: each test changes one field of the row and names it.
    private static NodeDef Node(
        int objectId = 1,
        string skill = "fighting",
        int level = 5,
        int tool = Sword,
        int xp = 10,
        int product = Logs,
        double chanceLow = 0.2,
        double chanceHigh = 0.8,
        int fullLevel = 15,
        double depleteChance = 0.1,
        int respawnTicks = 10) =>
        new(objectId, skill, level, tool, xp, product, chanceLow, chanceHigh, fullLevel, depleteChance, respawnTicks);

    private static NodeSet Build(IReadOnlyList<NodeDef> nodes) => new(nodes, Objects, Items(), Skills);

    // NodeDef.

    [Fact]
    public void SuccessChanceRisesInALineFromLevelToFullLevel()
    {
        var def = Node();

        Assert.Equal(0.2, def.ChanceAt(1));
        Assert.Equal(0.2, def.ChanceAt(5));
        Assert.Equal(0.5, def.ChanceAt(10), 1e-9);
        Assert.Equal(0.8, def.ChanceAt(15));
        Assert.Equal(0.8, def.ChanceAt(50));
    }

    [Fact]
    public void RejectsALevelOutsideRangeOrAFullLevelNotAboveIt()
    {
        Assert.ThrowsAny<ArgumentException>(() => Node(level: 0));
        Assert.ThrowsAny<ArgumentException>(() => Node(level: SkillDef.MaxLevel + 1));
        Assert.ThrowsAny<ArgumentException>(() => Node(fullLevel: 5));
        Assert.ThrowsAny<ArgumentException>(() => Node(fullLevel: SkillDef.MaxLevel + 1));
    }

    [Fact]
    public void RejectsAChanceOutside0To1AndAHighBelowTheLow()
    {
        Assert.ThrowsAny<ArgumentException>(() => Node(chanceLow: -0.1));
        Assert.ThrowsAny<ArgumentException>(() => Node(chanceHigh: 1.1));
        Assert.ThrowsAny<ArgumentException>(() => Node(depleteChance: 2));
        Assert.ThrowsAny<ArgumentException>(() => Node(depleteChance: double.NaN));
        Assert.ThrowsAny<ArgumentException>(() => Node(chanceLow: 0.6, chanceHigh: 0.5));
        Assert.Equal(1, Node(chanceLow: 1, chanceHigh: 1).ChanceAt(5));
    }

    [Fact]
    public void RejectsNoExperienceAndNoRespawn()
    {
        Assert.ThrowsAny<ArgumentException>(() => Node(xp: 0));
        Assert.ThrowsAny<ArgumentException>(() => Node(respawnTicks: 0));
    }

    // NodeSet.

    [Fact]
    public void FindsANodeByTheObjectItLivesOn()
    {
        var set = Build([Node(), Node(objectId: 2)]);

        Assert.Equal(2, set.Find(2)!.ObjectId);
        Assert.Null(set.Find(3));
    }

    [Fact]
    public void RejectsARepeatedObject()
    {
        Assert.ThrowsAny<ArgumentException>(() => Build([Node(), Node()]));
    }

    [Fact]
    public void RejectsAMissingObjectToolProductOrSkill()
    {
        Assert.ThrowsAny<ArgumentException>(() => Build([Node(objectId: 9)]));
        Assert.ThrowsAny<ArgumentException>(() => Build([Node(tool: 99)]));
        Assert.ThrowsAny<ArgumentException>(() => Build([Node(product: 99)]));
        Assert.ThrowsAny<ArgumentException>(() => Build([Node(skill: "baking")]));
    }
}
