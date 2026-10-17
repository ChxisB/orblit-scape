import 'package:orblit_net/orblit_net.dart' show GoneReason;
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_core/scape_core.dart';

import 'keys.dart';
import 'player_states.dart';

/// Keeps players between visits.
///
/// A save is captured on the tick and handed to the [SaveQueue], which writes
/// it somewhere else, so the tick never waits on a disk. A login reads the
/// save before the player joins, in [prepare], because the join itself is
/// one step on the tick and cannot wait.
final class Saves {
  Saves({
    required SaveQueue queue,
    required PlayerStates states,
    required AutoSave autosave,
    required bool Function(String player) hasSession,
    required void Function(String line) log,
  }) : _queue = queue,
       _states = states,
       _autosave = autosave,
       _hasSession = hasSession,
       _log = log;

  final SaveQueue _queue;
  final PlayerStates _states;
  final AutoSave _autosave;
  final bool Function(String player) _hasSession;
  final void Function(String line) _log;

  /// The state each player who is logging in will join with. A player who is
  /// admitted and never seated leaves theirs here until they log in again.
  final Map<String, PlayerState> _pending = {};

  /// How many saves have been queued for each key. A read that began before a
  /// save was queued may have found an older one, so it counts them.
  // shortcut: one small entry for each name saved in this run, never pruned.
  // Fine to a few hundred thousand names, then drop the keys of players who
  // have left and have nothing in flight.
  final Map<String, int> _queued = {};

  /// Reads the save of [player] so [arrived] can put it on them. Throws
  /// [FormatException] when the save is damaged, so a login is refused and a
  /// blank is never written over a real save.
  ///
  /// A save queued while this read was away, such as the one a player makes
  /// by leaving, may be newer than what the read found, so it reads again.
  /// Nothing after the last read waits, so no save can be queued before the
  /// host seats the player.
  Future<void> prepare(String player) async {
    final key = saveKeyOf(player);
    SaveRecord? record;
    int before;
    do {
      before = _queued[key] ?? 0;
      record = await _queue.read(key);
    } while (before != (_queued[key] ?? 0));
    // A player whose session is still held resumes with what the world has,
    // so a damaged save does not stand in the way of that.
    if (record == null || _hasSession(player)) {
      _pending.remove(player);
    } else {
      _pending[player] = PlayerState.decode(record.bytes);
    }
  }

  /// A player has joined. Puts the state [prepare] read onto them.
  // F5: the join is the moment the world takes it, and no caller needs it.
  void arrived(String player) {
    final state = _pending.remove(player);
    if (state == null) return;
    final dropped = _states.apply(player, state);
    if (dropped > 0) _log('$player: $dropped saved entries were left out');
  }

  /// A player's session ended. Call before the features forget them.
  void left(String player, GoneReason reason) {
    final reasonOfSave = switch (reason) {
      GoneReason.removed => const Leave(),
      GoneReason.disconnect || GoneReason.overBudget => const Disconnect(),
    };
    _save(player, reasonOfSave);
  }

  /// Saves the players the autosave names for [tick].
  void autosave(int tick) {
    for (final player in _autosave.claim(_states.players, tick)) {
      _save(player, const Autosave());
    }
  }

  /// Saves everyone in the world and waits for the store.
  Future<void> shutdown() async {
    for (final player in _states.players.toList()) {
      _save(player, const Shutdown());
    }
    final stuck = await _queue.close();
    if (stuck.isNotEmpty) _log('Not written: ${stuck.join(', ')}');
  }

  void _save(String player, SaveReason reason) {
    final state = _states.capture(player);
    if (state == null) return;
    final key = saveKeyOf(player);
    _queued[key] = (_queued[key] ?? 0) + 1;
    _queue.save(key, state.encode(), reason);
  }
}
