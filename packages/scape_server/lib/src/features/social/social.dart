import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';

/// Chat reaches listeners within this many tiles. A client is always sent the
/// 16 tiles round its player, so a listener this near has the speaker's
/// avatar to draw the line over.
const int chatRadius = 15;

/// Where a player stands and which entity is theirs, or null when they are not
/// in the world. Social asks it to decide who hears a line.
typedef Standing = ({int networkId, TileCoord tile})? Function(String player);

final class _Lists {
  final Set<String> friends = {};
  final Set<String> ignored = {};
}

/// Public chat, whispers, and each player's friend and ignore lists.
///
/// Lists and mutes are kept by name for as long as the server runs, so a
/// player who logs out and in again finds them. [listsOf] and [restore] let
/// a save carry the lists across a restart. Names on the wire are lower case,
/// the same as player keys.
final class Social {
  Social._(this._hosting, this._standing);

  /// Adds chat to [registry] and returns the feature, so a moderator can mute
  /// a player.
  factory Social.register(
    Registry registry,
    Services services, {
    required Standing standing,
  }) {
    final social = Social._(services.hosting, standing);
    registry
      ..addJoinHandler(social._join)
      ..addGoneHandler(social._gone)
      ..addIntentHandler(social._onIntent);
    return social;
  }

  final Hosting _hosting;
  final Standing _standing;
  final Set<String> _online = {};
  final Set<String> _muted = {};
  final Map<String, _Lists> _lists = {};

  void mute(String player) => _muted.add(nameKey(player));

  void unmute(String player) => _muted.remove(nameKey(player));

  bool isMuted(String player) => _muted.contains(nameKey(player));

  /// The friends and ignored names of [player], for a save.
  ({List<String> friends, List<String> ignored}) listsOf(String player) {
    final lists = _lists[player];
    return (friends: [...?lists?.friends], ignored: [...?lists?.ignored]);
  }

  /// Replaces the lists of [player] with a saved pair and sends them.
  void restore(
    String player, {
    required Iterable<String> friends,
    required Iterable<String> ignored,
  }) {
    _lists[player] = _Lists()
      ..friends.addAll(friends)
      ..ignored.addAll(ignored);
    _sendLists(player);
  }

  void _join(String key) {
    _online.add(key);
    _sendLists(key);
    _refreshWatchers(key);
  }

  void _gone(String key) {
    _online.remove(key);
    _refreshWatchers(key);
  }

  void _onIntent(String player, Intent intent) {
    if (!_online.contains(player)) return;
    switch (intent) {
      case Chat(:final text):
        _say(player, text);
      case Whisper(:final to, :final text):
        _whisper(player, to, text);
      case EditList(:final list, :final add, :final name):
        _edit(player, list, add, nameKey(name));
      case _:
        return;
    }
  }

  void _say(String player, String text) {
    final speaker = _standing(player);
    if (speaker == null || _refuseMuted(player)) return;
    final said = Said(speaker: speaker.networkId, text: text);
    for (final listener in _online) {
      if (_hears(listener, player, speaker.tile)) _hosting.tell(listener, said);
    }
  }

  bool _hears(String listener, String speaker, TileCoord at) {
    if (isIgnoring(listener, speaker)) return false;
    final tile = _standing(listener)?.tile;
    return tile != null &&
        tile.plane == at.plane &&
        (tile.x - at.x).abs() <= chatRadius &&
        (tile.y - at.y).abs() <= chatRadius;
  }

  void _whisper(String player, String to, String text) {
    if (_refuseMuted(player)) return;
    final target = nameKey(to);
    // An ignored sender gets the answer an offline name gets, so ignoring
    // does not tell them they were ignored.
    if (!_online.contains(target) || isIgnoring(target, player)) {
      _hosting.tell(player, Notice('That player is not online.'));
      return;
    }
    _hosting.tell(target, Whispered(from: player, text: text));
  }

  void _edit(String player, ListKind kind, bool add, String key) {
    final lists = _lists.putIfAbsent(player, _Lists.new);
    final names = kind == ListKind.friends ? lists.friends : lists.ignored;
    if (add) {
      final why = _whyNotAdded(player, names, key);
      if (why != null) {
        _hosting.tell(player, Notice(why));
        return;
      }
    }
    if (add ? names.add(key) : names.remove(key)) _sendLists(player);
  }

  String? _whyNotAdded(String player, Set<String> names, String key) {
    if (key == player) return 'You cannot add yourself.';
    if (!names.contains(key) && names.length >= maxListEntries) {
      return 'That list is full.';
    }
    return null;
  }

  bool _refuseMuted(String player) {
    if (!isMuted(player)) return false;
    _hosting.tell(player, Notice('You are muted.'));
    return true;
  }

  /// Whether [player] has [other] on their ignore list.
  bool isIgnoring(String player, String other) =>
      _lists[player]?.ignored.contains(other) ?? false;

  void _sendLists(String player) {
    final lists = _lists[player];
    _hosting.tell(
      player,
      SocialLists(
        friends: [
          for (final name in lists?.friends ?? const <String>{})
            (name: name, online: _online.contains(name)),
        ],
        ignored: [...?lists?.ignored],
      ),
    );
  }

  /// Players who list [key] as a friend see them come and go.
  void _refreshWatchers(String key) {
    for (final player in _online) {
      if (player != key && (_lists[player]?.friends.contains(key) ?? false)) {
        _sendLists(player);
      }
    }
  }
}
