namespace Scape.Core.Tests;

public class SkillsTests
{
    private static readonly XpCurve Curve = new(30, 4);

    private static SkillDef Def(string id, int cap = 50) => new(id, id, cap);

    // SkillDef.

    [Fact]
    public void RefusesABadIdAnEmptyNameAndACapOutOfRange()
    {
        foreach (var id in new[] { "", "Mining", "1mine", "a b", new string('a', 17) })
        {
            Assert.ThrowsAny<ArgumentException>(() => new SkillDef(id, "X", 10));
        }

        Assert.ThrowsAny<ArgumentException>(() => new SkillDef("mining", "", 10));
        Assert.ThrowsAny<ArgumentException>(() => new SkillDef("mining", "M", 0));
        Assert.ThrowsAny<ArgumentException>(() => new SkillDef("mining", "M", SkillDef.MaxLevel + 1));
    }

    // XpCurve.

    [Fact]
    public void Level1CostsNothingAndEveryLevelCostsMoreThanTheLast()
    {
        Assert.Equal(0, Curve.XpForLevel(1));
        for (var level = 2; level <= SkillDef.MaxLevel; level++)
        {
            Assert.True(Curve.XpForLevel(level) > Curve.XpForLevel(level - 1));
        }
    }

    [Fact]
    public void ALevelIsReachedAtItsCostAndNotOnePointBefore()
    {
        for (var level = 2; level <= 50; level++)
        {
            var cost = Curve.XpForLevel(level);
            Assert.Equal(level, Curve.LevelOf(cost, 50));
            Assert.Equal(level - 1, Curve.LevelOf(cost - 1, 50));
        }
    }

    [Fact]
    public void LevelNeverFallsAsExperienceRisesAndTheCapHolds()
    {
        var last = 1;
        for (var xp = 0; xp < 700000; xp += 37)
        {
            var level = Curve.LevelOf(xp, 50);
            Assert.True(level >= last);
            Assert.True(level <= 50);
            last = level;
        }

        Assert.Equal(50, Curve.LevelOf(0xFFFFFFFF, 50));
        Assert.Equal(1, Curve.LevelOf(-5, 50));
        Assert.Equal(1, Curve.LevelOf(1000000, 1));
    }

    [Fact]
    public void RefusesACurveWithNoQuadraticTermOrANegativeCubicOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new XpCurve(0, 1));
        Assert.ThrowsAny<ArgumentException>(() => new XpCurve(1, -1));
    }

    // SkillSet.

    [Fact]
    public void NamesEachSkillInTheSchemaAndFindsItById()
    {
        var set = new SkillSet(Curve, [Def("mining"), Def("fishing")]);

        Assert.Equal(2, set.Schema.SlotCount);
        Assert.True(set.Schema.Has("skill.mining.xp"));
        Assert.Equal("fishing", set.Find("fishing")!.Id);
        Assert.Null(set.Find("nothing"));
    }

    [Fact]
    public void RefusesASkillListedTwice()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SkillSet(Curve, [Def("mining"), Def("mining")]));
    }

    [Fact]
    public void RefusesACapWhoseExperienceDoesNotFit32Bits()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SkillSet(new XpCurve(100000, 100000), [Def("mining", 120)]));
    }

    [Fact]
    public void StopsExperienceAtTheCapLevelsCost()
    {
        var set = new SkillSet(Curve, [Def("mining", 10)]);

        Assert.Equal(Curve.XpForLevel(10), set.MaxXp(set.Find("mining")!));
    }
}
