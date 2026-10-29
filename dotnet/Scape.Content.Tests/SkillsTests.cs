namespace Scape.Content.Tests;

public class SkillsTests
{
    [Fact]
    public void TheFirstGameHasElevenSkillsEachCappedAtLevel50()
    {
        Assert.Equal(
            ["woodcutting", "mining", "fishing", "cooking", "smithing", "firemaking", "fletching", "fighting", "health", "magic", "prayer"],
            DefaultSkills.All.Skills.Select(s => s.Id));
        Assert.All(DefaultSkills.All.Skills, s => Assert.Equal(50, s.LevelCap));
    }

    [Fact]
    public void Level50CostsTheFigureTheDocCommentGives()
    {
        Assert.Equal(542626, DefaultSkills.All.Curve.XpForLevel(50));
    }
}
