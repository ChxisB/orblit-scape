import 'dart:typed_data';

import 'package:orblit_progress/orblit_progress.dart';
import 'package:orblit_quest/orblit_quest.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../combat/combat.dart';
import '../items/ground.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../skills/skills.dart';

String _completeText(Quest quest) => 'Quest complete: ${quest.title}';

/// Every player's place in every quest, and what a finished quest gives.
///
/// Each player's stages are a [ProgressTable]. A change tells only that
/// player. Only a conversation moves a quest, and it does so through
/// [commit].
final class Quests {
  Quests._({
    required Services services,
    required Story story,
    required Skills levels,
    required Combat combat,
    required Items bags,
    required GroundItems ground,
    required Movement movement,
  }) : _services = services,
       _story = story,
       _levels = levels,
       _combat = combat,
       _bags = bags,
       _ground = ground,
       _movement = movement;

  /// Adds quests to [registry] and returns the feature. Throws
  /// [ArgumentError] when a reward names a skill or an item the game does not
  /// have, or when a quest's title cannot be sent as a notice.
  factory Quests.register(
    Registry registry,
    Services services, {
    required Story story,
    required SkillSet skills,
    required ItemSet items,
    required Skills levels,
    required Combat combat,
    required Items bags,
    required GroundItems ground,
    required Movement movement,
  }) {
    for (final quest in story.book.quests) {
      if (!isValidText(_completeText(quest))) {
        throw ArgumentError.value(quest.title, quest.key, 'is not valid text');
      }
    }
    for (final reward in story.rewards.values) {
      for (final skill in reward.xp.keys) {
        if (skills.find(skill) == null) {
          throw ArgumentError.value(skill, 'rewards', 'is not a skill');
        }
      }
      for (final stack in reward.items) {
        items.require(stack.item);
      }
    }
    final feature = Quests._(
      services: services,
      story: story,
      levels: levels,
      combat: combat,
      bags: bags,
      ground: ground,
      movement: movement,
    );
    registry
      ..addJoinHandler(feature._join)
      ..addGoneHandler(feature._gone);
    return feature;
  }

  final Services _services;
  final Story _story;
  final Skills _levels;
  final Combat _combat;
  final Items _bags;
  final GroundItems _ground;
  final Movement _movement;
  final Map<String, ProgressTable> _tables = {};

  /// The table [player] has now, or null for a player who is not in the
  /// world. A table is never changed, so a caller can tell that it is still
  /// current by comparing it with `identical`.
  ProgressTable? tableOf(String player) => _tables[player];

  /// The stage of each quest [player] has begun, by quest key. Empty for a
  /// player who is not in the world.
  Map<String, int> stagesOf(String player) {
    final table = _tables[player];
    if (table == null) return const {};
    return {
      for (final quest in _story.book.quests)
        if (_story.book.stageOf(table, quest.key) > 0)
          quest.key: _story.book.stageOf(table, quest.key),
    };
  }

  /// Sets [player]'s stages from [saved] and tells them each one that is
  /// above zero. A quest the game no longer has, or a stage past the quest's
  /// last, is left out and counted in the result.
  // F5: the count is how the caller tells an operator what a save lost.
  int restore(String player, Map<String, int> saved) {
    final held = _tables[player];
    if (held == null) return saved.length;
    var table = held;
    var dropped = 0;
    for (final MapEntry(key: key, value: stage) in saved.entries) {
      final name = questStageName(key);
      if (_story.schema.has(name) && stage <= _story.book.quest(key).last) {
        table = table.set(name, stage).table;
      } else {
        dropped++;
      }
    }
    _replace(player, held, table);
    return dropped;
  }

  /// Makes [to] the table [player] has, tells them what changed and pays out
  /// each quest that [to] finishes and [from] did not. Returns false and
  /// changes nothing when [from] is not the table the player has now, so a
  /// conversation that began before their quests changed cannot write back
  /// stages it read. A player who is not in the world gets nothing.
  // F5: false is how the caller learns that the write was refused.
  bool commit(
    String player, {
    required ProgressTable from,
    required ProgressTable to,
  }) {
    if (!identical(_tables[player], from)) return false;
    _replace(player, from, to);
    final book = _story.book;
    for (final quest in book.quests) {
      if (!book.isComplete(from, quest.key) && book.isComplete(to, quest.key)) {
        _reward(player, quest.key);
      }
    }
    return true;
  }

  void _replace(String player, ProgressTable held, ProgressTable table) {
    _tables[player] = table;
    final changes = table.changesSince(held);
    if (changes.isEmpty) return;
    _services.hosting.tell(
      player,
      PrivateState(Uint8List.fromList([questsDeltaTag, ...changes.encode()])),
    );
  }

  /// The one place a finished quest pays out. What does not fit in the
  /// inventory lands at the player's feet, theirs for a while.
  void _reward(String player, String quest) {
    final reward = _story.rewards[quest];
    final tile = _movement.tileOf(player);
    final inventory = _bags.container(player, ContainerId.inventory);
    if (reward != null && tile != null && inventory != null) {
      var leveled = false;
      for (final MapEntry(key: skill, value: xp) in reward.xp.entries) {
        for (final level in _levels.grant(player, skill, xp)) {
          leveled = true;
          _tell(player, 'You reached level $level in $skill.');
        }
      }
      if (leveled) _combat.refit(player);
      for (final ItemStack(:item, :count) in reward.items) {
        final left = count - inventory.add(item, count);
        if (left > 0) _ground.drop(item, left, at: tile, owner: player);
      }
    }
    _tell(player, _completeText(_story.book.quest(quest)));
  }

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));

  void _join(String key) => _tables[key] = ProgressTable(_story.schema);

  void _gone(String key) => _tables.remove(key);
}
