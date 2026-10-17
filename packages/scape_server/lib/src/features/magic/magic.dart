import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../combat/combat.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../npcs/npcs.dart';
import '../skills/skills.dart';

/// Spells a player casts by name at a creature in range with a clear line. A
/// cast costs items and a wait, and gives experience in the magic skill. A
/// spell that is refused costs nothing, and none of them walks the caster
/// anywhere.
final class Magic {
  Magic._(
    this._services,
    this._spells, {
    required Skills skills,
    required Items bags,
    required Combat combat,
    required Movement movement,
    required Npcs npcs,
  }) : _skills = skills,
       _bags = bags,
       _combat = combat,
       _movement = movement,
       _npcs = npcs;

  /// Adds spells to [registry].
  factory Magic.register(
    Registry registry,
    Services services, {
    required SpellSet spells,
    required Skills skills,
    required Items bags,
    required Combat combat,
    required Movement movement,
    required Npcs npcs,
  }) {
    final feature = Magic._(
      services,
      spells,
      skills: skills,
      bags: bags,
      combat: combat,
      movement: movement,
      npcs: npcs,
    );
    registry.addIntentHandler(feature._onIntent);
    return feature;
  }

  final Services _services;
  final SpellSet _spells;
  final Skills _skills;
  final Items _bags;
  final Combat _combat;
  final Movement _movement;
  final Npcs _npcs;

  void _onIntent(String player, Intent intent) {
    if (intent is! Cast) return;
    final spell = _spells.find(intent.spell);
    if (spell == null) return;
    final target = _npcs.find(intent.target);
    final reason = _refusal(player, spell, target);
    if (reason != null) return _tell(player, reason);
    final inventory = _bags.container(player, ContainerId.inventory)!;
    if (inventory.exchange(take: spell.cost, give: const []) != Outcome.done) {
      return _tell(player, 'You do not have what that spell costs.');
    }
    _skills.award(player, _spells.skill, spell.xp);
    final level = _skills.levelOf(player, _spells.skill);
    _combat.cast(player, spell, level, at: target);
  }

  /// Why [player] may not cast [spell] at [target] now, or null.
  String? _refusal(String player, SpellDef spell, PlacedNpc? target) {
    if (_skills.levelOf(player, _spells.skill) < spell.level) {
      return 'You need level ${spell.level} in ${_spells.skill}.';
    }
    if (!_combat.isReady(player)) return 'You are not ready yet.';
    if (spell.effect is SpellTeleport) return null;
    if (target == null || target.def.fight == null || target.isDead) {
      return 'You cannot cast that there.';
    }
    final tile = _movement.tileOf(player)!; // isReady found them in the world
    final reach = Within(Footprint.single(target.tile), range: spell.range);
    return reachSatisfied(_services.map, reach, tile)
        ? null
        : 'That is out of range.';
  }

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));
}
