using Orblit.Dialogue;
using Orblit.Progress;
using Orblit.Quest;
using Scape.Core;

namespace Scape.Server;

/// <summary>What conversations ask of the rest of the server.</summary>
public sealed record TalkOptions
{
    public required Story Story { get; init; }

    /// <summary>The creatures, whose dialogue keys are checked when the server starts.</summary>
    public required NpcSet Npcs { get; init; }

    /// <summary>Decides when a conversation opens, since it is the talk option of a creature.</summary>
    public required Interactions Interactions { get; init; }

    public required Quests Quests { get; init; }

    public required Windows Windows { get; init; }
}

/// <summary>
/// Conversations. Talking to a creature that has a dialogue opens one, and the player goes on with
/// <see cref="Continue"/> or picks with <see cref="Choose"/>. A conversation moves a quest only
/// through a named step, so every move passes the quest book and a finished quest pays out.
/// </summary>
public sealed class Talk
{
    private const string NothingToSay = "They have nothing to say.";
    private const string NotOffered = "That is not on offer.";

    private readonly Services _services;
    private readonly TalkOptions _options;
    private readonly DialogueBook _book;
    private readonly Dictionary<string, DialogueRunner> _talks = [];

    private Talk(Services services, TalkOptions options, DialogueBook book)
    {
        _services = services;
        _options = options;
        _book = book;
    }

    /// <summary>Adds conversations to <paramref name="registry"/>.</summary>
    /// <exception cref="ArgumentException">
    /// A creature names a dialogue the story lacks, a dialogue writes the table itself or has text
    /// the client cannot show, or a dialogue uses a step or a name the story does not have.
    /// </exception>
    public static void Register(Registry registry, Services services, TalkOptions options)
    {
        var story = options.Story;
        Check(story, options.Npcs);
        var feature = new Talk(services, options, new DialogueBook(story.Schema, Hooks(story), story.Dialogues));
        options.Interactions.AddNpcAction(NpcOption.Talk, feature.Open);
        registry.AddIntentHandler(feature.OnIntent);
        registry.AddGoneHandler(player => feature._talks.Remove(player));
    }

    /// <summary>
    /// The functions a dialogue may name in <c>before</c>, one for each step of the story. A step
    /// the quest book refuses leaves the table as it was, so a conversation heard twice does
    /// nothing the second time.
    /// </summary>
    private static Dictionary<string, Before> Hooks(Story story) =>
        story.Steps.ToDictionary(
            step => step.Key,
            step => (Before)(table => story.Book.Advance(table, step.Value.Quest, step.Value.Stage) is Moved moved ? moved.Table : table));

    private static void Check(Story story, NpcSet npcs)
    {
        var keys = story.Dialogues.Select(dialogue => dialogue.Key).ToHashSet();
        foreach (var npc in npcs.Npcs.Where(npc => npc.Dialogue is { } key && !keys.Contains(key)))
        {
            throw new ArgumentException($"{npc.Name} names {npc.Dialogue}, which is not a dialogue.", nameof(npcs));
        }

        foreach (var dialogue in story.Dialogues)
        {
            foreach (var node in dialogue.Nodes.Values)
            {
                CheckNode(dialogue.Key, node);
            }
        }
    }

    private static void CheckNode(string dialogue, Node node)
    {
        IReadOnlyList<string> texts = node switch
        {
            LineNode line => [line.Speaker, line.Text],
            ChoiceNode choice when choice.Options.Count > Wire.MaxChoices =>
                throw new ArgumentException($"{node.Id} in {dialogue} offers too many choices.", nameof(node)),
            ChoiceNode choice => [.. choice.Options.Select(option => option.Text)],
            SetNode => throw new ArgumentException($"{node.Id} in {dialogue} writes a quest stage, which only a step may do.", nameof(node)),
            _ => [],
        };
        foreach (var text in texts.Where(text => !Wire.IsValidText(text)))
        {
            throw new ArgumentException($"{dialogue} has text that is not valid: {text}", nameof(node));
        }
    }

    private void Open(string player, PlacedNpc npc)
    {
        if (npc.Def.Dialogue is not { } key || _options.Quests.TableOf(player) is not { } table)
        {
            Tell(player, NothingToSay);
            return;
        }

        var runner = _book.Start(key, table);
        if (!Commit(player, table, runner) || runner.IsOver)
        {
            Tell(player, NothingToSay);
            return;
        }

        _talks[player] = runner;
        _options.Windows.Open(player, new OpenWindow(WindowKind.Talk, npc.Def.Id, new Beside(Footprint.OfTile(npc.Tile))));
        Show(player, runner);
    }

    private void OnIntent(string player, Intent intent)
    {
        if (!_talks.TryGetValue(player, out var runner) || _options.Windows.Of(player)?.Kind != WindowKind.Talk)
        {
            return;
        }

        switch (intent)
        {
            case Continue:
                Go(player, runner, runner.Current is LineNode, runner.Next);
                break;
            case Choose choose:
                Go(player, runner, choose.Index < runner.Options.Count, () => runner.Choose(choose.Index));
                break;
        }
    }

    /// <summary>
    /// Runs <paramref name="move"/> when <paramref name="allowed"/>, and ends the conversation
    /// instead when the player's quests changed under it, since the runner would write back a
    /// table that is out of date.
    /// </summary>
    private void Go(string player, DialogueRunner runner, bool allowed, Action move)
    {
        if (!allowed)
        {
            Tell(player, NotOffered);
            return;
        }

        var from = runner.Table;
        move();
        if (Commit(player, from, runner))
        {
            Show(player, runner);
        }
        else
        {
            End(player);
        }
    }

    private bool Commit(string player, ProgressTable from, DialogueRunner runner) =>
        _options.Quests.Commit(player, from, runner.Table);

    private void Show(string player, DialogueRunner runner)
    {
        switch (runner.Current)
        {
            case LineNode line:
                _services.Hosting.Tell(player, new ConversationLine(line.Speaker, line.Text));
                break;
            case ChoiceNode:
                _services.Hosting.Tell(player, new ConversationChoices([.. runner.Options.Select(option => option.Text)]));
                break;
            default:
                End(player);
                break;
        }
    }

    private void End(string player)
    {
        _talks.Remove(player);
        _options.Windows.Close(player);
    }

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));
}
