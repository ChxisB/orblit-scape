using Orblit.Quest;

namespace Scape.Core.Tests;

public class StoryTests
{
    private static QuestDefinition Quest(string key) =>
        new(key, key, [.. Enumerable.Range(0, 3).Select(i => new Stage($"s{i}", $"{i}"))]);

    private static Story Story(
        IReadOnlyDictionary<string, QuestStep>? steps = null,
        IReadOnlyDictionary<string, QuestReward>? rewards = null) =>
        new([Quest("lamp"), Quest("crabs")], [], steps, rewards);

    [Fact]
    public void GivesEachQuestAWholeInTheOrderTheQuestsWereGiven()
    {
        var schema = Story().Schema;

        Assert.Equal(2, schema.SlotCount);
        Assert.Equal(0, schema.PlaceOf(QuestNames.StageName("lamp")).Slot);
        Assert.Equal(1, schema.PlaceOf(QuestNames.StageName("crabs")).Slot);
    }

    [Fact]
    public void AStepMustNameAQuestAndAStageItReaches()
    {
        Assert.ThrowsAny<ArgumentException>(() => Story(steps: new Dictionary<string, QuestStep> { ["go"] = new("gone", 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Story(steps: new Dictionary<string, QuestStep> { ["go"] = new("lamp", 3) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Story(steps: new Dictionary<string, QuestStep> { ["go"] = new("lamp", 0) }));
        Assert.Single(Story(steps: new Dictionary<string, QuestStep> { ["go"] = new("lamp", 2) }).Steps);
    }

    [Fact]
    public void ARewardMustBelongToAQuestAndGiveSomething()
    {
        var xp = new Dictionary<string, int> { ["a"] = 1 };

        Assert.ThrowsAny<ArgumentException>(() => Story(rewards: new Dictionary<string, QuestReward> { ["gone"] = new(xp) }));
        Assert.ThrowsAny<ArgumentException>(() => new QuestReward());
        Assert.ThrowsAny<ArgumentException>(() => new QuestReward(new Dictionary<string, int> { ["a"] = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => new QuestReward(items: [.. Enumerable.Repeat(new ItemStack(1, 1), QuestReward.MaxItems + 1)]));
    }

    [Fact]
    public void KeepsWhatItWasBuiltFromEvenIfThatChangesAfterwards()
    {
        var steps = new Dictionary<string, QuestStep> { ["go"] = new("lamp", 1) };
        var built = Story(steps: steps);

        steps.Clear();

        Assert.Single(built.Steps);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, QuestStep>)built.Steps).Clear());
    }
}
