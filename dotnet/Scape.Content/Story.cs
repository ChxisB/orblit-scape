using Orblit.Dialogue;
using Orblit.Quest;
using Scape.Core;

namespace Scape.Content;

/// <summary>
/// Two quests that lean on each other. The Dark Lantern runs between Brindle
/// and the Guard. Goblin Count opens once it is done, so the Shopkeeper
/// offers the help only to a player who has finished the first.
/// </summary>
public static class DefaultStory
{
    private const string Lantern = """
        {
          "kind": "orblit.quest",
          "formatVersion": 1,
          "key": "lantern",
          "title": "The Dark Lantern",
          "stages": [
            {"id": "unknown", "journal": ""},
            {"id": "asked", "journal": "Brindle's tower lantern is dark. Ask the Guard if the tower is safe."},
            {"id": "warned", "journal": "The Guard says the tower is safe by day. Tell Brindle."},
            {"id": "lit", "journal": "Brindle lit the tower lantern."}
          ]
        }
        """;

    private const string Tally = """
        {
          "kind": "orblit.quest",
          "formatVersion": 1,
          "key": "tally",
          "title": "Goblin Count",
          "stages": [
            {"id": "unknown", "journal": ""},
            {
              "id": "asked",
              "journal": "The Shopkeeper wants to know how many goblins are about. Ask the Guard.",
              "requires": [{"name": "quest.lantern.stage", "op": ">=", "value": 3}]
            },
            {"id": "counted", "journal": "The Guard counted four goblins."}
          ]
        }
        """;

    private const string Brindle = """
        {
          "kind": "orblit.dialogue",
          "formatVersion": 1,
          "key": "brindle",
          "variants": [
            {"start": "after", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 3}]},
            {"start": "report", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 2}]},
            {"start": "waiting", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 1}]},
            {"start": "hello"}
          ],
          "nodes": {
            "hello": {"type": "line", "speaker": "Brindle", "text": "The tower lantern has been dark for three nights.", "next": "ask"},
            "ask": {
              "type": "choice",
              "options": [
                {"text": "I can help.", "next": "begin"},
                {"text": "Not now."}
              ]
            },
            "begin": {"type": "line", "speaker": "Brindle", "text": "Ask the Guard if the tower is safe. Then come back.", "before": "lantern-asked"},
            "waiting": {"type": "line", "speaker": "Brindle", "text": "Have you asked the Guard yet?"},
            "report": {"type": "line", "speaker": "Brindle", "text": "Safe by day? Then I will go up now.", "next": "thanks", "before": "lantern-lit"},
            "thanks": {"type": "line", "speaker": "Brindle", "text": "Take this for your trouble."},
            "after": {"type": "line", "speaker": "Brindle", "text": "The lantern burns bright. Thank you."}
          }
        }
        """;

    private const string Guard = """
        {
          "kind": "orblit.dialogue",
          "formatVersion": 1,
          "key": "guard",
          "variants": [
            {"start": "count", "when": [{"name": "quest.tally.stage", "op": ">=", "value": 1}]},
            {"start": "again", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 2}]},
            {"start": "warn", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 1}]},
            {"start": "hello"}
          ],
          "nodes": {
            "hello": {"type": "line", "speaker": "Guard", "text": "Move along."},
            "warn": {"type": "line", "speaker": "Guard", "text": "The tower is safe by day. Tell the lamplighter.", "before": "lantern-warned"},
            "again": {"type": "line", "speaker": "Guard", "text": "The tower is still safe by day."},
            "count": {"type": "line", "speaker": "Guard", "text": "Four goblins, last I counted. Mind them.", "before": "tally-counted"}
          }
        }
        """;

    private const string Shopkeeper = """
        {
          "kind": "orblit.dialogue",
          "formatVersion": 1,
          "key": "shopkeeper",
          "variants": [
            {"start": "after", "when": [{"name": "quest.tally.stage", "op": ">=", "value": 2}]},
            {"start": "waiting", "when": [{"name": "quest.tally.stage", "op": ">=", "value": 1}]},
            {"start": "hello"}
          ],
          "nodes": {
            "hello": {"type": "line", "speaker": "Shopkeeper", "text": "Tools for sale. Press Trade to see them.", "next": "ask"},
            "ask": {
              "type": "choice",
              "options": [
                {"text": "Can I help?", "next": "begin", "when": [{"name": "quest.lantern.stage", "op": ">=", "value": 3}]},
                {"text": "Just looking."}
              ]
            },
            "begin": {"type": "line", "speaker": "Shopkeeper", "text": "Ask the Guard how many goblins are about.", "before": "tally-asked"},
            "waiting": {"type": "line", "speaker": "Shopkeeper", "text": "Have you asked the Guard about the goblins?"},
            "after": {"type": "line", "speaker": "Shopkeeper", "text": "Four. That is fewer than last month. Thank you."}
          }
        }
        """;

    public static Story Story { get; } = new(
        [QuestDefinition.Parse(Lantern), QuestDefinition.Parse(Tally)],
        [DialogueDefinition.Parse(Brindle), DialogueDefinition.Parse(Guard), DialogueDefinition.Parse(Shopkeeper)],
        new Dictionary<string, QuestStep>
        {
            ["lantern-asked"] = new("lantern", 1),
            ["lantern-warned"] = new("lantern", 2),
            ["lantern-lit"] = new("lantern", 3),
            ["tally-asked"] = new("tally", 1),
            ["tally-counted"] = new("tally", 2),
        },
        new Dictionary<string, QuestReward>
        {
            ["lantern"] = new(xp: new Dictionary<string, int> { ["firemaking"] = 80 }, items: [new ItemStack(ItemId.Coins, 25)]),
            ["tally"] = new(xp: new Dictionary<string, int> { ["fighting"] = 60 }, items: [new ItemStack(ItemId.Coins, 40)]),
        });
}
