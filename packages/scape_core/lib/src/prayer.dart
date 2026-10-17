import 'skills.dart';

/// The highest prayer id.
const int maxPrayerId = 0xFF;

/// The combat numbers a prayer may raise.
enum CombatStat { attack, strength, defence }

/// One prayer: a row of content. While it is on it raises [stat] by [percent]
/// of the player's level and drains [drain] each tick.
final class PrayerDef {
  PrayerDef({
    required this.id,
    required this.name,
    required this.level,
    required this.stat,
    required this.percent,
    required this.drain,
  }) {
    if (id < 1 || id > maxPrayerId) {
      throw ArgumentError.value(id, 'id', 'is outside 1 to $maxPrayerId');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (level < 1 || level > maxLevel) {
      throw ArgumentError.value(level, 'level', 'is outside 1 to $maxLevel');
    }
    if (percent < 1 || drain < 1) {
      throw ArgumentError('A prayer raises a stat and drains points.');
    }
  }

  final int id;
  final String name;
  final int level;
  final CombatStat stat;
  final int percent;

  /// Drain each tick, in units of which [PrayerSet.drainPerPoint] make one
  /// point.
  final int drain;
}

/// The prayers a game has. A player has one point for each level of [skill].
/// The drain of the prayers that are on adds up each tick, and each
/// [drainPerPoint] of it costs a point.
final class PrayerSet {
  PrayerSet({
    required this.skill,
    required List<PrayerDef> prayers,
    required SkillSet skills,
    this.drainPerPoint = 100,
  }) : prayers = List.unmodifiable(prayers),
       _byId = {for (final prayer in prayers) prayer.id: prayer} {
    final def = skills.find(skill);
    if (def == null) throw ArgumentError.value(skill, 'skill', 'is unknown');
    if (_byId.length != prayers.length) {
      throw ArgumentError.value(prayers, 'prayers', 'repeats an id');
    }
    if (drainPerPoint < 1) {
      throw ArgumentError.value(drainPerPoint, 'drainPerPoint', 'is below one');
    }
    for (final prayer in prayers) {
      if (prayer.level > def.levelCap) {
        throw ArgumentError.value(
          prayer.id,
          'prayers',
          'needs a level too high',
        );
      }
    }
  }

  /// The id of the skill whose level is a player's points.
  final String skill;
  final List<PrayerDef> prayers;
  final int drainPerPoint;
  final Map<int, PrayerDef> _byId;

  PrayerDef? find(int id) => _byId[id];
}
