import 'package:scape_core/scape_core.dart';

import 'items.dart';
import 'skills.dart';

abstract final class SpellId {
  static const int strike = 1;
  static const int daze = 2;
  static const int home = 3;
}

/// The spells of the first game. Each costs runes. Strike hurts, daze keeps a
/// creature from swinging for a few ticks, and home puts the caster back at
/// the first tile of the world.
final SpellSet defaultSpells = SpellSet(
  skill: 'magic',
  items: defaultItems,
  skills: defaultSkills,
  spells: [
    SpellDef(
      id: SpellId.strike,
      name: 'Strike',
      level: 1,
      effect: SpellDamage(4),
      cost: [ItemStack(ItemId.runes, 1)],
      xp: 6,
    ),
    SpellDef(
      id: SpellId.daze,
      name: 'Daze',
      level: 5,
      effect: SpellStun(6),
      cost: [ItemStack(ItemId.runes, 2)],
      speed: 6,
      xp: 10,
    ),
    SpellDef(
      id: SpellId.home,
      name: 'Home teleport',
      level: 10,
      effect: SpellTeleport(TileCoord(32, 32, 0)),
      cost: [ItemStack(ItemId.runes, 3)],
      xp: 20,
    ),
  ],
);
