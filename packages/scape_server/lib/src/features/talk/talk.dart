import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:orblit_progress/orblit_progress.dart';
import 'package:orblit_quest/orblit_quest.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../npcs/npcs.dart';
import '../quests/quests.dart';
import '../windows/windows.dart';

const String _nothingToSay = 'They have nothing to say.';
const String _notOffered = 'That is not on offer.';

/// Conversations. Talking to a creature that has a dialogue opens one, and
/// the player goes on with [Continue] or picks with [Choose]. A conversation
/// moves a quest only through a named step, so every move passes the quest
/// book and a finished quest pays out.
final class Talk {
  Talk._({
    required Services services,
    required DialogueBook book,
    required Quests quests,
    required Windows windows,
  }) : _services = services,
       _book = book,
       _quests = quests,
       _windows = windows;

  /// Adds conversations to [registry]. Throws [ArgumentError] when a
  /// creature names a dialogue the story lacks, when a dialogue writes the
  /// table itself or has text the client cannot show, or when a dialogue uses
  /// a step or a name the story does not have.
  static void register(
    Registry registry,
    Services services, {
    required Story story,
    required NpcSet npcs,
    required Interactions interactions,
    required Quests quests,
    required Windows windows,
  }) {
    _check(story, npcs);
    final feature = Talk._(
      services: services,
      book: DialogueBook(
        schema: story.schema,
        hooks: _hooks(story),
        dialogues: story.dialogues,
      ),
      quests: quests,
      windows: windows,
    );
    interactions.addNpcAction(NpcOption.talk, feature._open);
    registry
      ..addIntentHandler(feature._onIntent)
      ..addGoneHandler(feature._talks.remove);
  }

  final Services _services;
  final DialogueBook _book;
  final Quests _quests;
  final Windows _windows;
  final Map<String, DialogueRunner> _talks = {};

  void _open(String player, PlacedNpc npc) {
    final key = npc.def.dialogue;
    final table = _quests.tableOf(player);
    if (key == null || table == null) {
      _tell(player, _nothingToSay);
      return;
    }
    final runner = _book.start(key, table);
    if (!_commit(player, table, runner) || runner.isOver) {
      _tell(player, _nothingToSay);
      return;
    }
    _talks[player] = runner;
    _windows.open(
      player,
      WindowKind.talk,
      ref: npc.def.id,
      reach: Beside(Footprint.single(npc.tile)),
    );
    _show(player, runner);
  }

  void _onIntent(String player, Intent intent) {
    final runner = _talks[player];
    if (runner == null || _windows.of(player)?.kind != WindowKind.talk) return;
    switch (intent) {
      case Continue():
        _go(player, runner, runner.current is LineNode, runner.next);
      case Choose(:final index):
        _go(
          player,
          runner,
          index < runner.options.length,
          () => runner.choose(index),
        );
      default:
    }
  }

  /// Runs [move] when [allowed], and ends the conversation instead when the
  /// player's quests changed under it, since the runner would write back a
  /// table that is out of date.
  void _go(
    String player,
    DialogueRunner runner,
    bool allowed,
    void Function() move,
  ) {
    if (!allowed) {
      _tell(player, _notOffered);
      return;
    }
    final from = runner.table;
    move();
    if (_commit(player, from, runner)) {
      _show(player, runner);
    } else {
      _end(player);
    }
  }

  bool _commit(String player, ProgressTable from, DialogueRunner runner) =>
      _quests.commit(player, from: from, to: runner.table);

  void _show(String player, DialogueRunner runner) {
    switch (runner.current) {
      case LineNode(:final speaker, :final text):
        _services.hosting.tell(
          player,
          ConversationLine(speaker: speaker, text: text),
        );
      case ChoiceNode():
        _services.hosting.tell(
          player,
          ConversationChoices([for (final o in runner.options) o.text]),
        );
      default:
        _end(player);
    }
  }

  void _end(String player) {
    _talks.remove(player);
    _windows.close(player);
  }

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));
}

/// The functions a dialogue may name in `before`, one for each step of the
/// story. A step the quest book refuses leaves the table as it was, so a
/// conversation heard twice does nothing the second time.
Map<String, Before> _hooks(Story story) => {
  for (final MapEntry(:key, value: (:quest, :stage)) in story.steps.entries)
    key: (table) => switch (story.book.advance(table, quest, stage)) {
      Moved(table: final moved) => moved,
      Refused() => table,
    },
};

void _check(Story story, NpcSet npcs) {
  final keys = {for (final d in story.dialogues) d.key};
  for (final npc in npcs.npcs) {
    final key = npc.dialogue;
    if (key != null && !keys.contains(key)) {
      throw ArgumentError.value(key, npc.name, 'is not a dialogue');
    }
  }
  for (final dialogue in story.dialogues) {
    for (final node in dialogue.nodes.values) {
      _checkNode(dialogue.key, node);
    }
  }
}

void _checkNode(String dialogue, Node node) {
  final texts = switch (node) {
    LineNode(:final speaker, :final text) => [speaker, text],
    ChoiceNode(:final options) => [for (final o in options) o.text],
    SetNode() => throw ArgumentError.value(
      node.id,
      dialogue,
      'writes a quest stage, which only a step may do',
    ),
    JumpNode() => const <String>[],
  };
  if (node is ChoiceNode && node.options.length > maxChoices) {
    throw ArgumentError.value(node.id, dialogue, 'offers too many choices');
  }
  for (final text in texts) {
    if (!isValidText(text)) {
      throw ArgumentError.value(text, dialogue, 'is not valid text');
    }
  }
}
