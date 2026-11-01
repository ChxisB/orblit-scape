namespace Scape.Core;

/// <summary>The states of a node object.</summary>
public static class NodeState
{
    /// <summary>The state of a node object that can be gathered from.</summary>
    public const int Full = 0;

    /// <summary>The state of a node object that has run dry until it respawns.</summary>
    public const int Depleted = 1;
}

/// <summary>
/// One kind of resource a player gathers from: a tree, a rock, a fishing
/// spot. It is a row of content that names the world object it lives on.
/// </summary>
public sealed class NodeDef
{
    // F2: a row of content names its fields.
    public NodeDef(
        int objectId,
        string skill,
        int level,
        int tool,
        int xp,
        int product,
        double chanceLow,
        double chanceHigh,
        int fullLevel,
        double depleteChance,
        int respawnTicks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, SkillDef.MaxLevel);
        if (fullLevel <= level || fullLevel > SkillDef.MaxLevel)
        {
            throw new ArgumentException(FormattableString.Invariant($"The full level {fullLevel} is not above the level."), nameof(fullLevel));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(xp, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(respawnTicks, 1);
        CheckChance(chanceLow, nameof(chanceLow));
        CheckChance(chanceHigh, nameof(chanceHigh));
        CheckChance(depleteChance, nameof(depleteChance));
        if (chanceHigh < chanceLow)
        {
            throw new ArgumentException("The high chance is below the low chance.", nameof(chanceHigh));
        }

        ObjectId = objectId;
        Skill = skill;
        Level = level;
        Tool = tool;
        Xp = xp;
        Product = product;
        ChanceLow = chanceLow;
        ChanceHigh = chanceHigh;
        FullLevel = fullLevel;
        DepleteChance = depleteChance;
        RespawnTicks = respawnTicks;
    }

    /// <summary>The id of the <see cref="ObjectDef"/> this node is.</summary>
    public int ObjectId { get; }

    public string Skill { get; }

    /// <summary>The skill level needed to gather.</summary>
    public int Level { get; }

    /// <summary>The item the player must hold or wear.</summary>
    public int Tool { get; }

    /// <summary>Experience for each success.</summary>
    public int Xp { get; }

    /// <summary>The item a success gives.</summary>
    public int Product { get; }

    /// <summary>The chance of a success at <see cref="Level"/>, from 0 to 1.</summary>
    public double ChanceLow { get; }

    /// <summary>The chance of a success at <see cref="FullLevel"/> and above.</summary>
    public double ChanceHigh { get; }

    public int FullLevel { get; }

    /// <summary>The chance, after a success, that the node runs dry.</summary>
    public double DepleteChance { get; }

    /// <summary>Ticks a depleted node stays dry.</summary>
    public int RespawnTicks { get; }

    /// <summary>
    /// The chance that one try succeeds for a player of <paramref name="skillLevel"/>. It
    /// rises in a straight line from <see cref="ChanceLow"/> at <see cref="Level"/> to
    /// <see cref="ChanceHigh"/> at <see cref="FullLevel"/>.
    /// </summary>
    public double ChanceAt(int skillLevel)
    {
        if (skillLevel >= FullLevel)
        {
            return ChanceHigh;
        }

        if (skillLevel <= Level)
        {
            return ChanceLow;
        }

        var along = (double)(skillLevel - Level) / (FullLevel - Level);
        return ChanceLow + (ChanceHigh - ChanceLow) * along;
    }

    private static void CheckChance(double chance, string name)
    {
        if (double.IsNaN(chance) || chance < 0 || chance > 1)
        {
            throw new ArgumentException(FormattableString.Invariant($"{chance} is outside 0 to 1."), name);
        }
    }
}

/// <summary>
/// The nodes a game has. Each must name an object, items and a skill that
/// exist, so a typo fails when the game starts and not when a player tries.
/// </summary>
public sealed class NodeSet
{
    private readonly Dictionary<int, NodeDef> _byObject;

    public NodeSet(IReadOnlyList<NodeDef> nodes, ObjectSet objects, ItemSet items, SkillSet skills)
    {
        Nodes = [.. nodes];
        _byObject = Nodes.DistinctBy(n => n.ObjectId).ToDictionary(n => n.ObjectId);
        if (_byObject.Count != Nodes.Count)
        {
            throw new ArgumentException("The nodes repeat an object.", nameof(nodes));
        }

        foreach (var node in Nodes)
        {
            objects.Require(node.ObjectId);
            items.Require(node.Tool);
            items.Require(node.Product);
            if (skills.Find(node.Skill) is null)
            {
                throw new ArgumentException($"{node.Skill} is unknown.", nameof(skills));
            }
        }
    }

    public IReadOnlyList<NodeDef> Nodes { get; }

    /// <summary>The node that lives on the object <paramref name="objectId"/>, or null.</summary>
    public NodeDef? Find(int objectId) => _byObject.GetValueOrDefault(objectId);
}
