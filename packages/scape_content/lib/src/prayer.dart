import 'package:scape_core/scape_core.dart';

import 'skills.dart';

abstract final class PrayerId {
  static const int thickSkin = 1;
  static const int burstOfStrength = 2;
  static const int clarity = 3;
}

/// The prayers of the first game. A player has one point for each prayer
/// level, and a prayer that drains 5 a tick spends a point every 20 ticks.
final PrayerSet defaultPrayers = PrayerSet(
  skill: 'prayer',
  skills: defaultSkills,
  prayers: [
    PrayerDef(
      id: PrayerId.thickSkin,
      name: 'Thick skin',
      level: 1,
      stat: CombatStat.defence,
      percent: 10,
      drain: 5,
    ),
    PrayerDef(
      id: PrayerId.burstOfStrength,
      name: 'Burst of strength',
      level: 4,
      stat: CombatStat.strength,
      percent: 10,
      drain: 5,
    ),
    PrayerDef(
      id: PrayerId.clarity,
      name: 'Clarity of thought',
      level: 7,
      stat: CombatStat.attack,
      percent: 10,
      drain: 5,
    ),
  ],
);
