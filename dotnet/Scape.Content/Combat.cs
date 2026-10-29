using Scape.Core;

namespace Scape.Content;

/// <summary>The fight rules of the first game, on the shared defaults.</summary>
public static class DefaultCombat
{
    public static CombatRules Rules { get; } = new(fightingSkill: "fighting", healthSkill: "health");
}
