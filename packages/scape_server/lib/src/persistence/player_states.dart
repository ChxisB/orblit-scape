import 'package:scape_core/scape_core.dart';

import '../features/combat/combat.dart';
import '../features/items/items.dart';
import '../features/movement/movement.dart';
import '../features/quests/quests.dart';
import '../features/skills/skills.dart';
import '../features/social/social.dart';

/// Moves what the features hold of a player into a [PlayerState] and back.
/// This is the one place that knows which features a save covers.
final class PlayerStates {
  PlayerStates({
    required Movement movement,
    required Skills skills,
    required Items items,
    required Social social,
    required Combat combat,
    required TileMap map,
    Quests? quests,
  }) : _movement = movement,
       _skills = skills,
       _items = items,
       _social = social,
       _combat = combat,
       _quests = quests,
       _map = map;

  final Movement _movement;
  final Skills _skills;
  final Items _items;
  final Social _social;
  final Combat _combat;
  final Quests? _quests;
  final TileMap _map;

  /// The keys of the players in the world.
  Iterable<String> get players => _movement.players;

  /// Throws [FormatException] when [state] holds something this server would
  /// lose on the next save, so a login is refused and the save stays as it
  /// was.
  void check(PlayerState state) {
    if (_quests == null && state.quests.isNotEmpty) {
      throw const FormatException(
        'The save has quests and this server has no story',
      );
    }
  }

  /// What the features hold of [player], or null when they are not in the
  /// world.
  PlayerState? capture(String player) {
    final tile = _movement.tileOf(player);
    if (tile == null) return null;
    final lists = _social.listsOf(player);
    return PlayerState(
      tile: tile,
      skills: _skills.experienceOf(player),
      containers: {
        for (final id in ContainerId.values)
          if (_items.container(player, id) case final container?)
            id: container.slots,
      },
      friends: lists.friends,
      ignored: lists.ignored,
      quests: _quests?.stagesOf(player) ?? const {},
    );
  }

  /// Puts [state] on [player], who has just joined. A saved tile that is no
  /// longer open ground leaves them at the spawn. Returns how many saved
  /// skills, stacks and quests the game no longer has.
  // F5: the count is how the caller tells an operator what a save lost.
  int apply(String player, PlayerState state) {
    if (!_map.flagsAt(state.tile).has(TileFlags.blocked | TileFlags.water)) {
      _movement.teleport(player, state.tile);
    }
    var dropped = _skills.restore(player, state.skills);
    for (final MapEntry(key: id, value: slots) in state.containers.entries) {
      dropped += _items.container(player, id)?.restore(slots) ?? slots.length;
    }
    dropped += _quests?.restore(player, state.quests) ?? state.quests.length;
    _social.restore(player, friends: state.friends, ignored: state.ignored);
    _combat.refit(player);
    return dropped;
  }
}
