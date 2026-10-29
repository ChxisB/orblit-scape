using Scape.Core;

namespace Scape.Content;

/// <summary>The ids of the prayers in <see cref="DefaultPrayers"/>.</summary>
public static class PrayerId
{
    public const int ThickSkin = 1;
    public const int BurstOfStrength = 2;
    public const int Clarity = 3;
}

/// <summary>
/// The prayers of the first game. A player has one point for each prayer
/// level, and a prayer that drains 5 a tick spends a point every 20 ticks.
/// </summary>
public static class DefaultPrayers
{
    public static PrayerSet All { get; } = new(
        "prayer",
        [
            new PrayerDef(PrayerId.ThickSkin, "Thick skin", level: 1, CombatStat.Defence, percent: 10, drain: 5),
            new PrayerDef(PrayerId.BurstOfStrength, "Burst of strength", level: 4, CombatStat.Strength, percent: 10, drain: 5),
            new PrayerDef(PrayerId.Clarity, "Clarity of thought", level: 7, CombatStat.Attack, percent: 10, drain: 5),
        ],
        DefaultSkills.All);
}
