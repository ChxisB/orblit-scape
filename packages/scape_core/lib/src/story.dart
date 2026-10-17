import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:orblit_progress/orblit_progress.dart';
import 'package:orblit_quest/orblit_quest.dart';

import 'items.dart';

/// The tag of a [PrivateState] delta that carries a change to the quest table.
const int questsDeltaTag = 3;

/// The most items one reward may give.
const int maxRewardItems = 8;

/// What a dialogue's `before` name does: moves [quest] to [stage].
typedef QuestStep = ({String quest, int stage});

/// What a player gets for finishing a quest: experience by skill id, and
/// items.
final class QuestReward {
  QuestReward({
    Map<String, int> xp = const {},
    List<ItemStack> items = const [],
  }) : xp = Map.unmodifiable(xp),
       items = List.unmodifiable(items) {
    if (xp.isEmpty && items.isEmpty) {
      throw ArgumentError('A reward gives experience or items.');
    }
    if (items.length > maxRewardItems) {
      throw ArgumentError.value(items.length, 'items', 'is over the cap');
    }
    for (final MapEntry(:key, :value) in xp.entries) {
      if (value < 1) throw ArgumentError.value(value, key, 'is below one');
    }
  }

  final Map<String, int> xp;
  final List<ItemStack> items;
}

/// The quests and conversations of a game. A quest is a stage in a table with
/// one whole for each quest, so a game that adds a quest at the end leaves
/// every saved stage where it was.
final class Story {
  /// Throws [ArgumentError] when a step or a reward names a quest the game
  /// does not have, or a step names a stage the quest does not reach.
  factory Story({
    required List<Quest> quests,
    required List<Dialogue> dialogues,
    Map<String, QuestStep> steps = const {},
    Map<String, QuestReward> rewards = const {},
  }) {
    final schema = ProgressSchema(
      version: 1,
      names: [for (final quest in quests) Whole(questStageName(quest.key))],
    );
    return Story._(
      schema,
      QuestBook(schema: schema, quests: quests),
      List.unmodifiable(dialogues),
      Map.unmodifiable(steps),
      Map.unmodifiable(rewards),
    );
  }

  Story._(this.schema, this.book, this.dialogues, this.steps, this.rewards) {
    for (final MapEntry(key: name, value: (:quest, :stage)) in steps.entries) {
      RangeError.checkValueInInterval(stage, 1, book.quest(quest).last, name);
    }
    for (final key in rewards.keys) {
      book.quest(key);
    }
  }

  /// The table every player's quests live in.
  final ProgressSchema schema;
  final QuestBook book;
  final List<Dialogue> dialogues;

  /// The functions a dialogue file may name in `before`, by name.
  final Map<String, QuestStep> steps;

  /// What each quest gives when it completes, by quest key.
  final Map<String, QuestReward> rewards;
}
