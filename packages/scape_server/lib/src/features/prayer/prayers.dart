import 'dart:math';

import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../skills/skills.dart';

/// What one player has done with their prayer points.
final class _Pool {
  /// Points spent. The points left are the prayer level less this.
  int lost = 0;

  /// Drain that has not yet made a whole point.
  int drained = 0;

  /// The prayers that are on, in the order they were switched on.
  final Set<int> active = {};

  /// The most points the player was last told they have.
  int shownMax = 0;
}

/// Prayers a player switches on for a bonus to their fighting. Each one
/// drains the prayer points a player has, which equal their prayer level when
/// they log in, and every prayer goes off when the points reach zero. One
/// prayer is on for each stat, and switching on another for the same stat
/// replaces it.
final class Prayers {
  Prayers._(this._services, this._set, this._skills);

  /// Adds prayers to [registry] and returns the feature, so combat can ask
  /// for the bonus.
  factory Prayers.register(
    Registry registry,
    Services services, {
    required PrayerSet prayers,
    required Skills skills,
  }) {
    final feature = Prayers._(services, prayers, skills);
    registry
      ..addIntentHandler(feature._onIntent)
      ..addJoinHandler(feature._join)
      ..addGoneHandler(feature._pools.remove)
      ..addSystem(Phase.combat, feature._drain);
    return feature;
  }

  final Services _services;
  final PrayerSet _set;
  final Skills _skills;
  final Map<String, _Pool> _pools = {};

  /// The percent the prayer that is on for [stat] adds to [player]'s level in
  /// it, or zero.
  int percentOf(String player, CombatStat stat) =>
      _active(
        player,
      ).where((prayer) => prayer.stat == stat).firstOrNull?.percent ??
      0;

  Iterable<PrayerDef> _active(String player) => [
    for (final id in _pools[player]?.active ?? const <int>{}) ?_set.find(id),
  ];

  int _max(String player) => _skills.levelOf(player, _set.skill);

  int _points(String player, _Pool pool) => max(0, _max(player) - pool.lost);

  void _join(String player) {
    _pools[player] = _Pool();
    _show(player);
  }

  void _onIntent(String player, Intent intent) {
    if (intent is! TogglePrayer) return;
    final pool = _pools[player];
    final prayer = _set.find(intent.prayer);
    if (pool == null || prayer == null) return;
    if (!pool.active.remove(prayer.id)) {
      final reason = _refusal(player, pool, prayer);
      if (reason != null) return _tell(player, reason);
      pool.active
        ..removeWhere((id) => _set.find(id)?.stat == prayer.stat)
        ..add(prayer.id);
    }
    _show(player);
  }

  String? _refusal(String player, _Pool pool, PrayerDef prayer) {
    if (_skills.levelOf(player, _set.skill) < prayer.level) {
      return 'You need level ${prayer.level} in ${_set.skill}.';
    }
    return _points(player, pool) < 1 ? 'You have no prayer points.' : null;
  }

  /// Spends the drain of every prayer that is on. A prayer that runs the
  /// points out switches them all off. A player whose level changed since
  /// they were last told, by a login, a quest or a level, is told again.
  void _drain() {
    for (final MapEntry(key: player, value: pool) in _pools.entries) {
      pool.drained += _active(player).fold(0, (sum, p) => sum + p.drain);
      final spent = pool.drained ~/ _set.drainPerPoint;
      pool.drained -= spent * _set.drainPerPoint;
      pool.lost += spent;
      if (spent > 0 && _points(player, pool) == 0) _runOut(player, pool);
      if (spent > 0 || pool.shownMax != _max(player)) _show(player);
    }
  }

  void _runOut(String player, _Pool pool) {
    pool.active.clear();
    pool.drained = 0;
    _tell(player, 'You have run out of prayer points.');
  }

  void _show(String player) {
    final pool = _pools[player]!; // every caller has just read it
    pool.shownMax = _max(player);
    _services.hosting.tell(
      player,
      PrayerView(
        points: _points(player, pool),
        max: _max(player),
        active: pool.active.toList(),
      ),
    );
  }

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));
}
