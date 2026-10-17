import 'package:scape_core/scape_core.dart';

/// The skills of the first game, and the curve that turns experience into a
/// level. Level 50 costs 542,626 experience on this curve.
final SkillSet defaultSkills = SkillSet(
  curve: XpCurve(quadratic: 30, cubic: 4),
  skills: [
    SkillDef(id: 'woodcutting', name: 'Woodcutting', levelCap: 50),
    SkillDef(id: 'mining', name: 'Mining', levelCap: 50),
    SkillDef(id: 'fishing', name: 'Fishing', levelCap: 50),
    SkillDef(id: 'cooking', name: 'Cooking', levelCap: 50),
    SkillDef(id: 'smithing', name: 'Smithing', levelCap: 50),
    SkillDef(id: 'fighting', name: 'Fighting', levelCap: 50),
    SkillDef(id: 'health', name: 'Health', levelCap: 50),
  ],
);
