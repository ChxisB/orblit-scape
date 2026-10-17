import 'dart:typed_data';

import 'package:orblit_progress/orblit_progress.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';

/// Experience in each skill, for every player in the world.
///
/// Each player's experience is a [ProgressTable]. A grant tells only that
/// player which skills changed. The table is lost when the player goes, until
/// accounts save it.
final class Skills {
  Skills._(this._hosting, this._set);

  /// Adds skills to [registry] and returns the feature, so gathering,
  /// cooking and combat can grant experience.
  factory Skills.register(
    Registry registry,
    Services services, {
    required SkillSet skills,
  }) {
    final feature = Skills._(services.hosting, skills);
    registry
      ..addJoinHandler(feature._join)
      ..addGoneHandler(feature._gone);
    return feature;
  }

  final Hosting _hosting;
  final SkillSet _set;
  final Map<String, ProgressTable> _tables = {};

  /// The experience [player] has in [skill], or zero for a player who is not
  /// in the world. Throws [ArgumentError] for a skill that does not exist.
  int xpOf(String player, String skill) =>
      _tables[player]?.get(SkillSet.xpName(_def(skill).id)) ?? 0;

  /// The level of [player] in [skill]. A player who is not in the world is
  /// level 1.
  int levelOf(String player, String skill) {
    final def = _def(skill);
    return _set.levelOf(def, xpOf(player, skill));
  }

  /// Adds [xp] to [player]'s [skill] and returns each level reached, once
  /// each and lowest first. Experience stops at the level cap. A player who
  /// is not in the world gains nothing. Throws [ArgumentError] for a skill
  /// that does not exist and [RangeError] for a negative [xp].
  // F5: the levels reached are the caller's cue to announce them.
  List<int> grant(String player, String skill, int xp) {
    final def = _def(skill);
    if (xp < 0) throw RangeError.value(xp, 'xp', 'is negative');
    final table = _tables[player];
    if (table == null) return const [];
    final name = SkillSet.xpName(def.id);
    final before = table.get(name);
    final after = before + xp > _set.maxXp(def) ? _set.maxXp(def) : before + xp;
    if (after == before) return const [];
    final (table: next, :changes) = table.set(name, after);
    _tables[player] = next;
    _hosting.tell(
      player,
      PrivateState(Uint8List.fromList([skillsDeltaTag, ...changes.encode()])),
    );
    return [
      for (
        var level = _set.levelOf(def, before) + 1;
        level <= _set.levelOf(def, after);
        level++
      )
        level,
    ];
  }

  SkillDef _def(String skill) =>
      _set.find(skill) ??
      (throw ArgumentError.value(skill, 'skill', 'is unknown'));

  void _join(String key) => _tables[key] = ProgressTable(_set.schema);

  void _gone(String key) => _tables.remove(key);
}
