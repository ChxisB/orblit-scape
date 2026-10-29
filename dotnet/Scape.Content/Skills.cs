using Scape.Core;

namespace Scape.Content;

/// <summary>
/// The skills of the first game, and the curve that turns experience into a
/// level. Level 50 costs 542,626 experience on this curve.
/// </summary>
public static class DefaultSkills
{
    public static SkillSet All { get; } = new(
        new XpCurve(quadratic: 30, cubic: 4),
        [
            new SkillDef("woodcutting", "Woodcutting", levelCap: 50),
            new SkillDef("mining", "Mining", levelCap: 50),
            new SkillDef("fishing", "Fishing", levelCap: 50),
            new SkillDef("cooking", "Cooking", levelCap: 50),
            new SkillDef("smithing", "Smithing", levelCap: 50),
            new SkillDef("firemaking", "Firemaking", levelCap: 50),
            new SkillDef("fletching", "Fletching", levelCap: 50),
            new SkillDef("fighting", "Fighting", levelCap: 50),
            new SkillDef("health", "Health", levelCap: 50),
            new SkillDef("magic", "Magic", levelCap: 50),
            new SkillDef("prayer", "Prayer", levelCap: 50),
        ]);
}
