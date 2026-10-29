using Orblit.Progress;
using Orblit.Quest;
using Scape.Core;

namespace Scape.Server;

/// <summary>What quests ask of the rest of the server.</summary>
public sealed record QuestsOptions
{
    public required Story Story { get; init; }

    /// <summary>The skills a reward may name.</summary>
    public required SkillSet SkillSet { get; init; }

    public required ItemSet Items { get; init; }

    public required Skills Skills { get; init; }

    /// <summary>Refits a player whose reward raised a level.</summary>
    public required Combat Combat { get; init; }

    public required Items Bags { get; init; }

    public required GroundItems Ground { get; init; }

    public required Movement Movement { get; init; }
}

/// <summary>
/// Every player's place in every quest, and what a finished quest gives. Each player's stages are
/// a <see cref="ProgressTable"/>. A change tells only that player. Only a conversation moves a
/// quest, and it does so through <see cref="Commit"/>.
/// </summary>
public sealed class Quests
{
    private readonly Services _services;
    private readonly QuestsOptions _options;
    private readonly Dictionary<string, ProgressTable> _tables = [];

    private Quests(Services services, QuestsOptions options)
    {
        _services = services;
        _options = options;
    }

    private QuestBook Book => _options.Story.Book;

    /// <summary>Adds quests to <paramref name="registry"/> and returns the feature.</summary>
    /// <exception cref="ArgumentException">
    /// A reward names a skill or an item the game does not have, or a quest's title cannot be
    /// sent as a notice.
    /// </exception>
    public static Quests Register(Registry registry, Services services, QuestsOptions options)
    {
        CheckTitles(options.Story);
        CheckRewards(options);
        var feature = new Quests(services, options);
        registry.AddJoinHandler(player => feature._tables[player] = new ProgressTable(options.Story.Schema));
        registry.AddGoneHandler(player => feature._tables.Remove(player));
        return feature;
    }

    /// <summary>
    /// The table <paramref name="player"/> has now, or null for a player who is not in the world.
    /// A table is never changed, so a caller can tell that it is still current by reference.
    /// </summary>
    public ProgressTable? TableOf(string player) => _tables.GetValueOrDefault(player);

    /// <summary>
    /// The stage of each quest <paramref name="player"/> has begun, by quest key. Empty for a
    /// player who is not in the world.
    /// </summary>
    public IReadOnlyDictionary<string, long> StagesOf(string player)
    {
        if (!_tables.TryGetValue(player, out var table))
        {
            return new Dictionary<string, long>();
        }

        return Book.Quests
            .Select(quest => (quest.Key, Stage: Book.StageOf(table, quest.Key)))
            .Where(quest => quest.Stage > 0)
            .ToDictionary(quest => quest.Key, quest => quest.Stage);
    }

    /// <summary>
    /// Sets <paramref name="player"/>'s stages from <paramref name="saved"/> and tells them each one
    /// that is above zero. A quest the game no longer has, or a stage past the quest's last, is
    /// left out.
    /// </summary>
    /// <returns>How many saved quests were left out. A player who is not in the world leaves out all of them.</returns>
    // F5: the count is how the caller tells an operator what a save lost.
    public int Restore(string player, IReadOnlyDictionary<string, long> saved)
    {
        if (!_tables.TryGetValue(player, out var held))
        {
            return saved.Count;
        }

        var table = held;
        var dropped = 0;
        foreach (var (key, stage) in saved)
        {
            var name = QuestNames.StageName(key);
            if (_options.Story.Schema.Has(name) && stage <= Book.Quest(key).Last)
            {
                table = table.Set(name, stage).Table;
            }
            else
            {
                dropped++;
            }
        }

        Replace(player, held, table);
        return dropped;
    }

    /// <summary>
    /// Makes <paramref name="to"/> the table <paramref name="player"/> has, tells them what changed
    /// and pays out each quest that <paramref name="to"/> finishes and <paramref name="from"/> did
    /// not. Changes nothing when <paramref name="from"/> is not the table the player has now, so a
    /// conversation that began before their quests changed cannot write back stages it read.
    /// </summary>
    /// <returns>False when the write was refused.</returns>
    // F5: false is how the caller learns that the write was refused.
    public bool Commit(string player, ProgressTable from, ProgressTable to)
    {
        if (!ReferenceEquals(_tables.GetValueOrDefault(player), from))
        {
            return false;
        }

        Replace(player, from, to);
        foreach (var quest in Book.Quests.Where(quest => !Book.IsComplete(from, quest.Key) && Book.IsComplete(to, quest.Key)))
        {
            Reward(player, quest);
        }

        return true;
    }

    private static string CompleteText(QuestDefinition quest) => $"Quest complete: {quest.Title}";

    private static void CheckTitles(Story story)
    {
        foreach (var quest in story.Book.Quests.Where(quest => !Wire.IsValidText(CompleteText(quest))))
        {
            throw new ArgumentException($"The title of {quest.Key} is not valid text.", nameof(story));
        }
    }

    private static void CheckRewards(QuestsOptions options)
    {
        foreach (var reward in options.Story.Rewards.Values)
        {
            foreach (var skill in reward.Xp.Keys.Where(skill => options.SkillSet.Find(skill) is null))
            {
                throw new ArgumentException($"{skill} is not a skill.", nameof(options));
            }

            foreach (var stack in reward.Items)
            {
                options.Items.Require(stack.Item);
            }
        }
    }

    private void Replace(string player, ProgressTable held, ProgressTable table)
    {
        _tables[player] = table;
        var changes = table.ChangesSince(held);
        if (!changes.IsEmpty)
        {
            _services.Hosting.Tell(player, new PrivateState([Story.DeltaTag, .. changes.Encode()]));
        }
    }

    /// <summary>
    /// The one place a finished quest pays out. What does not fit in the inventory lands at the
    /// player's feet, theirs for a while.
    /// </summary>
    private void Reward(string player, QuestDefinition quest)
    {
        if (_options.Story.Rewards.GetValueOrDefault(quest.Key) is { } reward
            && _options.Movement.TileOf(player) is { } tile
            && _options.Bags.ContainerOf(player, ContainerId.Inventory) is { } inventory)
        {
            Pay(player, reward, tile, inventory);
        }

        _services.Hosting.Tell(player, new Notice(CompleteText(quest)));
    }

    private void Pay(string player, QuestReward reward, TileCoord tile, Container inventory)
    {
        var levelled = false;
        foreach (var (skill, xp) in reward.Xp)
        {
            levelled |= _options.Skills.Award(player, skill, xp).Count > 0;
        }

        if (levelled)
        {
            _options.Combat.Refit(player);
        }

        foreach (var stack in reward.Items)
        {
            // Never more than the stack's own count, so it fits an int.
            var left = (int)(stack.Count - inventory.Add(stack.Item, stack.Count));
            if (left > 0)
            {
                _options.Ground.Drop(stack.Item, left, tile, player);
            }
        }
    }
}
