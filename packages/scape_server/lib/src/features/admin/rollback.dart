import 'package:orblit_save/orblit_save.dart';

import '../../persistence/keys.dart';
import 'ledger.dart';

/// Puts a player's save back to how it was some minutes ago, from the
/// history the store keeps. The player is logged out first, because the game
/// writes a live player's save over anything put there.
final class Rollback {
  Rollback({
    required HistoryStore history,
    required SaveQueue queue,
    required Ledger ledger,
    required Future<void> Function(String player) logOut,
    required bool Function(String player) isOnline,
    required DateTime Function() now,
  }) : _history = history,
       _queue = queue,
       _ledger = ledger,
       _logOut = logOut,
       _isOnline = isOnline,
       _now = now;

  final HistoryStore _history;
  final SaveQueue _queue;
  final Ledger _ledger;
  final Future<void> Function(String player) _logOut;
  final bool Function(String player) _isOnline;
  final DateTime Function() _now;

  /// Rolls [player] back [minutes] and says what happened. A refusal is an
  /// answer, so this does not throw. The player cannot log in while it runs.
  Future<String> run(String player, int minutes) async {
    if (_ledger.isLocked(player)) {
      return 'A rollback of $player is already running.';
    }
    _ledger.lock(player);
    try {
      return await _restore(player, minutes);
    } on Object catch (error) {
      return 'The rollback of $player failed: $error';
    } finally {
      _ledger.unlock(player);
    }
  }

  Future<String> _restore(String player, int minutes) async {
    await _logOut(player);
    // The save the leave just queued is the newest version, and it must be
    // on the store before the restore moves the current save.
    final key = saveKeyOf(player);
    if ((await _queue.flush()).contains(key)) {
      return 'The save of $player is not on the store yet. Try again.';
    }
    final cutoff = _now().subtract(Duration(minutes: minutes));
    final versions = await _history.history.list(key);
    final target = versions.where((v) => !v.at.isAfter(cutoff)).firstOrNull;
    if (target == null) return '$player has no save from $minutes minutes ago.';
    final tools = SaveTools(
      store: _history,
      isLive: (_) => _isOnline(player),
      clock: _now,
    );
    await tools.restore(key, target.at);
    return '$player was rolled back to ${target.at.toIso8601String()}.';
  }
}
