namespace Scape.Core;

/// <summary>The combat numbers a prayer may raise.</summary>
public enum CombatStat
{
    Attack,
    Strength,
    Defence,
}

/// <summary>
/// One prayer: a row of content. While it is on it raises <see cref="Stat"/> by
/// <see cref="Percent"/> of the player's level and drains <see cref="Drain"/> each tick.
/// </summary>
public sealed class PrayerDef
{
    /// <summary>The highest prayer id.</summary>
    public const int MaxId = 0xFF;

    // F2: a row of content names its fields.
    public PrayerDef(int id, string name, int level, CombatStat stat, int percent, int drain)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, SkillDef.MaxLevel);
        if (percent < 1 || drain < 1)
        {
            throw new ArgumentException("A prayer raises a stat and drains points.");
        }

        Id = id;
        Name = name;
        Level = level;
        Stat = stat;
        Percent = percent;
        Drain = drain;
    }

    public int Id { get; }

    public string Name { get; }

    public int Level { get; }

    public CombatStat Stat { get; }

    public int Percent { get; }

    /// <summary>Drain each tick, in units of which <see cref="PrayerSet.DrainPerPoint"/> make one point.</summary>
    public int Drain { get; }
}

/// <summary>
/// The prayers a game has. A player has one point for each level of <see cref="Skill"/>.
/// The drain of the prayers that are on adds up each tick, and each
/// <see cref="DrainPerPoint"/> of it costs a point.
/// </summary>
public sealed class PrayerSet
{
    private readonly Dictionary<int, PrayerDef> _byId;

    public PrayerSet(string skill, IReadOnlyList<PrayerDef> prayers, SkillSet skills, int drainPerPoint = 100)
    {
        var def = skills.Find(skill) ?? throw new ArgumentException($"{skill} is unknown.", nameof(skill));
        Prayers = [.. prayers];
        _byId = Prayers.DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        if (_byId.Count != Prayers.Count)
        {
            throw new ArgumentException("The prayers repeat an id.", nameof(prayers));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(drainPerPoint, 1);
        if (Prayers.FirstOrDefault(p => p.Level > def.LevelCap) is { } high)
        {
            throw new ArgumentException(FormattableString.Invariant($"Prayer {high.Id} needs a level too high."), nameof(prayers));
        }

        Skill = skill;
        DrainPerPoint = drainPerPoint;
    }

    /// <summary>The id of the skill whose level is a player's points.</summary>
    public string Skill { get; }

    public IReadOnlyList<PrayerDef> Prayers { get; }

    public int DrainPerPoint { get; }

    public PrayerDef? Find(int id) => _byId.GetValueOrDefault(id);
}
