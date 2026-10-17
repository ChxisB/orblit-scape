import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:orblit_quest/orblit_quest.dart';
import 'package:scape_core/scape_core.dart';

import 'items.dart';

const String _lantern = '''
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
''';

const String _tally = '''
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
''';

const String _brindle = '''
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
''';

const String _guard = '''
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
''';

const String _shopkeeper = '''
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
''';

/// Two quests that lean on each other. The Dark Lantern runs between Brindle
/// and the Guard. Goblin Count opens once it is done, so the Shopkeeper
/// offers the help only to a player who has finished the first.
final Story defaultStory = Story(
  quests: [Quest.parse(_lantern), Quest.parse(_tally)],
  dialogues: [
    Dialogue.parse(_brindle),
    Dialogue.parse(_guard),
    Dialogue.parse(_shopkeeper),
  ],
  steps: {
    'lantern-asked': (quest: 'lantern', stage: 1),
    'lantern-warned': (quest: 'lantern', stage: 2),
    'lantern-lit': (quest: 'lantern', stage: 3),
    'tally-asked': (quest: 'tally', stage: 1),
    'tally-counted': (quest: 'tally', stage: 2),
  },
  rewards: {
    'lantern': QuestReward(
      xp: {'firemaking': 80},
      items: [ItemStack(ItemId.coins, 25)],
    ),
    'tally': QuestReward(
      xp: {'fighting': 60},
      items: [ItemStack(ItemId.coins, 40)],
    ),
  },
);
