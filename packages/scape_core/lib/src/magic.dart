import 'combat.dart';
import 'items.dart';
import 'skills.dart';
import 'tile_coord.dart';

/// The highest spell id.
const int maxSpellId = 0xFFFF;

/// What a spell does when it works.
sealed class SpellEffect {
  const SpellEffect();
}

/// Hurts the creature it is cast at by up to [most].
final class SpellDamage extends SpellEffect {
  SpellDamage(this.most) {
    if (most < 1) throw ArgumentError.value(most, 'most', 'is below one');
  }

  final int most;
}

/// Keeps the creature it is cast at from swinging for [ticks] ticks.
final class SpellStun extends SpellEffect {
  SpellStun(this.ticks) {
    if (ticks < 1) throw ArgumentError.value(ticks, 'ticks', 'is below one');
  }

  final int ticks;
}

/// Moves the caster to [to].
final class SpellTeleport extends SpellEffect {
  const SpellTeleport(this.to);

  final TileCoord to;
}

/// One spell: a row of content. It costs [cost] from the inventory, needs
/// [level] in the magic skill and gives [xp] in it.
final class SpellDef {
  SpellDef({
    required this.id,
    required this.name,
    required this.level,
    required this.effect,
    this.cost = const [],
    this.range = 8,
    this.speed = 5,
    this.xp = 0,
  }) {
    if (id < 1 || id > maxSpellId) {
      throw ArgumentError.value(id, 'id', 'is outside 1 to $maxSpellId');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (level < 1 || level > maxLevel) {
      throw ArgumentError.value(level, 'level', 'is outside 1 to $maxLevel');
    }
    if (range < 1 || range > maxRange || speed < 1 || xp < 0) {
      throw ArgumentError(
        'A spell has a range and a speed, and no negative xp.',
      );
    }
    if (cost.map((stack) => stack.item).toSet().length != cost.length) {
      throw ArgumentError.value(cost, 'cost', 'repeats an item');
    }
  }

  final int id;
  final String name;
  final int level;
  final SpellEffect effect;
  final List<ItemStack> cost;

  /// Tiles from the caster to a creature it may be cast at.
  final int range;

  /// Ticks after a cast before the caster may swing, shoot or cast again.
  final int speed;
  final int xp;
}

/// The spells a game has, and the skill they need. A typo in the skill or in
/// the item a spell costs fails when the game starts.
final class SpellSet {
  SpellSet({
    required this.skill,
    required List<SpellDef> spells,
    required ItemSet items,
    required SkillSet skills,
  }) : spells = List.unmodifiable(spells),
       _byId = {for (final spell in spells) spell.id: spell} {
    final def = skills.find(skill);
    if (def == null) throw ArgumentError.value(skill, 'skill', 'is unknown');
    if (_byId.length != spells.length) {
      throw ArgumentError.value(spells, 'spells', 'repeats an id');
    }
    for (final spell in spells) {
      if (spell.level > def.levelCap) {
        throw ArgumentError.value(spell.id, 'spells', 'needs a level too high');
      }
      for (final stack in spell.cost) {
        items.require(stack.item);
      }
    }
  }

  /// The id of the skill that spells need.
  final String skill;
  final List<SpellDef> spells;
  final Map<int, SpellDef> _byId;

  SpellDef? find(int id) => _byId[id];
}
