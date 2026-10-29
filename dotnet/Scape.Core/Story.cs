using Orblit.Dialogue;
using Orblit.Progress;
using Orblit.Quest;

namespace Scape.Core;

/// <summary>What a dialogue's <c>before</c> name does: moves <paramref name="Quest"/> to <paramref name="Stage"/>.</summary>
public readonly record struct QuestStep(string Quest, int Stage);

/// <summary>What a player gets for finishing a quest: experience by skill id, and items.</summary>
public sealed class QuestReward
{
    /// <summary>The most items one reward may give.</summary>
    public const int MaxItems = 8;

    public QuestReward(IReadOnlyDictionary<string, int>? xp = null, IReadOnlyList<ItemStack>? items = null)
    {
        Xp = new Dictionary<string, int>(xp ?? new Dictionary<string, int>()).AsReadOnly();
        Items = [.. items ?? []];
        if (Xp.Count == 0 && Items.Count == 0)
        {
            throw new ArgumentException("A reward gives experience or items.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Items.Count, MaxItems, nameof(items));
        if (Xp.FirstOrDefault(e => e.Value < 1) is { Key: { } skill })
        {
            throw new ArgumentException($"The experience in {skill} is below one.", nameof(xp));
        }
    }

    public IReadOnlyDictionary<string, int> Xp { get; }

    public IReadOnlyList<ItemStack> Items { get; }
}

/// <summary>
/// The quests and conversations of a game. A quest is a stage in a table with
/// one whole for each quest, so a game that adds a quest at the end leaves
/// every saved stage where it was.
/// </summary>
public sealed class Story
{
    /// <summary>The tag of a private state delta that carries a change to the quest table.</summary>
    public const int DeltaTag = 3;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when a step or a reward names a quest the game
    /// does not have, or a step names a stage the quest does not reach.
    /// </summary>
    public Story(
        IReadOnlyList<QuestDefinition> quests,
        IReadOnlyList<DialogueDefinition> dialogues,
        IReadOnlyDictionary<string, QuestStep>? steps = null,
        IReadOnlyDictionary<string, QuestReward>? rewards = null)
    {
        Schema = new ProgressSchema(1, [.. quests.Select(q => new Whole(QuestNames.StageName(q.Key)))]);
        Book = new QuestBook(Schema, quests);
        Dialogues = [.. dialogues];
        Steps = new Dictionary<string, QuestStep>(steps ?? new Dictionary<string, QuestStep>()).AsReadOnly();
        Rewards = new Dictionary<string, QuestReward>(rewards ?? new Dictionary<string, QuestReward>()).AsReadOnly();
        foreach (var (name, step) in Steps)
        {
            var last = Book.Quest(step.Quest).Last;
            ArgumentOutOfRangeException.ThrowIfLessThan(step.Stage, 1, name);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(step.Stage, last, name);
        }

        foreach (var key in Rewards.Keys)
        {
            Book.Quest(key);
        }
    }

    /// <summary>The table every player's quests live in.</summary>
    public ProgressSchema Schema { get; }

    public QuestBook Book { get; }

    public IReadOnlyList<DialogueDefinition> Dialogues { get; }

    /// <summary>The functions a dialogue file may name in <c>before</c>, by name.</summary>
    public IReadOnlyDictionary<string, QuestStep> Steps { get; }

    /// <summary>What each quest gives when it completes, by quest key.</summary>
    public IReadOnlyDictionary<string, QuestReward> Rewards { get; }
}
