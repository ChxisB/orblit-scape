using Orblit.Progress;
using Orblit.Quest;

namespace Scape.Content.Tests;

public class StoryTests
{
    [Fact]
    public void EveryCreatureThatTalksHasADialogueInTheStory()
    {
        var keys = DefaultStory.Story.Dialogues.Select(d => d.Key).ToHashSet();
        var named = DefaultNpcs.All.Npcs.Select(npc => npc.Dialogue).OfType<string>().ToList();

        Assert.Equal(3, named.Count);
        Assert.Subset(keys, named.ToHashSet());
    }

    [Fact]
    public void EveryRewardNamesASkillAndItemsTheGameHas()
    {
        foreach (var reward in DefaultStory.Story.Rewards.Values)
        {
            Assert.All(reward.Xp.Keys, skill => Assert.NotNull(DefaultSkills.All.Find(skill)));
            Assert.All(reward.Items, stack => Assert.NotNull(DefaultItems.All.Find(stack.Item)));
        }
    }

    [Fact]
    public void GoblinCountStaysShutUntilTheDarkLanternIsDone()
    {
        var book = DefaultStory.Story.Book;
        var fresh = new ProgressTable(DefaultStory.Story.Schema);

        var early = book.Advance(fresh, "tally", 1);
        var lit = book.Advance(fresh, "lantern", 3);
        var late = book.Advance(((Moved)lit).Table, "tally", 1);

        Assert.IsType<Refused>(early);
        Assert.IsType<Moved>(late);
    }
}
