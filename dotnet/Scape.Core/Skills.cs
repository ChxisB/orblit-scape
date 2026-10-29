using System.Text.RegularExpressions;
using Orblit.Progress;

namespace Scape.Core;

/// <summary>One skill: a row of content.</summary>
public sealed partial class SkillDef
{
    /// <summary>The highest level cap a skill may have.</summary>
    public const int MaxLevel = 120;

    public SkillDef(string id, string name, int levelCap)
    {
        if (!IdPattern().IsMatch(id))
        {
            throw new ArgumentException($"{id} is not a skill id.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(levelCap, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(levelCap, MaxLevel);
        Id = id;
        Name = name;
        LevelCap = levelCap;
    }

    public string Id { get; }

    public string Name { get; }

    public int LevelCap { get; }

    [GeneratedRegex(@"^[a-z][a-z0-9]{0,15}\z")]
    private static partial Regex IdPattern();
}

/// <summary>
/// Experience for a level, with whole numbers only so a server and a client
/// agree on every platform. Level 1 costs nothing. Level <c>n + 1</c> costs
/// <c>quadratic * n^2 + cubic * n^3</c>, which rises at every level.
/// </summary>
public sealed class XpCurve
{
    public XpCurve(int quadratic, int cubic)
    {
        if (quadratic < 1 || cubic < 0)
        {
            throw new ArgumentException("A curve needs a quadratic term above zero.");
        }

        Quadratic = quadratic;
        Cubic = cubic;
    }

    public int Quadratic { get; }

    public int Cubic { get; }

    public long XpForLevel(int level)
    {
        long n = level - 1;
        return Quadratic * n * n + Cubic * n * n * n;
    }

    /// <summary>The highest level up to <paramref name="cap"/> that <paramref name="xp"/> has paid for.</summary>
    public int LevelOf(long xp, int cap)
    {
        var low = 1;
        var high = cap;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (XpForLevel(middle) <= xp)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}

/// <summary>
/// The skills a game has and the curve they share. Experience is stored in a
/// <see cref="ProgressSchema"/> with one whole per skill, so a server and a
/// client that build the same set read the same bytes.
/// </summary>
public sealed class SkillSet
{
    /// <summary>The first byte of a private state delta that carries skill experience.</summary>
    public const int DeltaTag = 1;

    public SkillSet(XpCurve curve, IReadOnlyList<SkillDef> skills)
    {
        Skills = [.. skills];
        Schema = new ProgressSchema(1, [.. Skills.Select(s => new Whole(XpName(s.Id)))]);
        foreach (var skill in Skills)
        {
            if (curve.XpForLevel(skill.LevelCap) > 0xFFFFFFFF)
            {
                throw new ArgumentException($"{skill.Id} needs more than 32 bits.", nameof(skills));
            }
        }

        Curve = curve;
    }

    public XpCurve Curve { get; }

    public IReadOnlyList<SkillDef> Skills { get; }

    public ProgressSchema Schema { get; }

    /// <summary>The name <paramref name="skill"/>'s experience has in <see cref="Schema"/>.</summary>
    public static string XpName(string skill) => $"skill.{skill}.xp";

    public SkillDef? Find(string id) => Skills.FirstOrDefault(s => s.Id == id);

    /// <summary>The most experience <paramref name="skill"/> holds. It stops at the level cap.</summary>
    public long MaxXp(SkillDef skill) => Curve.XpForLevel(skill.LevelCap);

    public int LevelOf(SkillDef skill, long xp) => Curve.LevelOf(xp, skill.LevelCap);
}
