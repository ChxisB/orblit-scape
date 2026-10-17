import 'package:orblit_progress/orblit_progress.dart';

final RegExp _skillId = RegExp(r'^[a-z][a-z0-9]{0,15}$');

/// The highest level cap a skill may have.
const int maxLevel = 120;

/// The first byte of a [PrivateState] delta that carries skill experience.
const int skillsDeltaTag = 1;

/// One skill: a row of content.
final class SkillDef {
  SkillDef({required this.id, required this.name, required this.levelCap}) {
    if (!_skillId.hasMatch(id)) {
      throw ArgumentError.value(id, 'id', 'is not a skill id');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (levelCap < 1 || levelCap > maxLevel) {
      throw ArgumentError.value(
        levelCap,
        'levelCap',
        'is outside 1 to $maxLevel',
      );
    }
  }

  final String id;
  final String name;
  final int levelCap;
}

/// Experience for a level, with whole numbers only so a server and a client
/// agree on every platform. Level 1 costs nothing. Level `n + 1` costs
/// `quadratic * n^2 + cubic * n^3`, which rises at every level.
final class XpCurve {
  XpCurve({required this.quadratic, required this.cubic}) {
    if (quadratic < 1 || cubic < 0) {
      throw ArgumentError('A curve needs a quadratic term above zero.');
    }
  }

  final int quadratic;
  final int cubic;

  int xpForLevel(int level) {
    final n = level - 1;
    return quadratic * n * n + cubic * n * n * n;
  }

  /// The highest level up to [cap] that [xp] has paid for.
  int levelOf(int xp, int cap) {
    var low = 1;
    var high = cap;
    while (low < high) {
      final middle = (low + high + 1) ~/ 2;
      if (xpForLevel(middle) <= xp) {
        low = middle;
      } else {
        high = middle - 1;
      }
    }
    return low;
  }
}

/// The skills a game has and the curve they share. Experience is stored in a
/// [ProgressSchema] with one whole per skill, so a server and a client that
/// build the same set read the same bytes.
final class SkillSet {
  SkillSet({required this.curve, required List<SkillDef> skills})
    : skills = List.unmodifiable(skills),
      schema = ProgressSchema(
        version: 1,
        names: [for (final skill in skills) Whole(xpName(skill.id))],
      ) {
    for (final skill in skills) {
      if (curve.xpForLevel(skill.levelCap) > 0xFFFFFFFF) {
        throw ArgumentError.value(
          skill.id,
          'skills',
          'needs more than 32 bits',
        );
      }
    }
  }

  final XpCurve curve;
  final List<SkillDef> skills;
  final ProgressSchema schema;

  /// The name [skill]'s experience has in [schema].
  static String xpName(String skill) => 'skill.$skill.xp';

  SkillDef? find(String id) {
    for (final skill in skills) {
      if (skill.id == id) return skill;
    }
    return null;
  }

  /// The most experience [skill] holds. It stops at the level cap.
  int maxXp(SkillDef skill) => curve.xpForLevel(skill.levelCap);

  int levelOf(SkillDef skill, int xp) => curve.levelOf(xp, skill.levelCap);
}
