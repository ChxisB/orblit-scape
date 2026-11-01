namespace Scape.Core;

/// <summary>What a spell does when it works.</summary>
public abstract class SpellEffect
{
    // Closed to this assembly, so the cases below are every effect.
    private protected SpellEffect()
    {
    }
}

/// <summary>Hurts the creature it is cast at by up to <see cref="Most"/>.</summary>
public sealed class SpellDamage : SpellEffect
{
    public SpellDamage(int most)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);
        Most = most;
    }

    public int Most { get; }
}

/// <summary>Keeps the creature it is cast at from swinging for <see cref="Ticks"/> ticks.</summary>
public sealed class SpellStun : SpellEffect
{
    public SpellStun(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        Ticks = ticks;
    }

    public int Ticks { get; }
}

/// <summary>Moves the caster to <see cref="To"/>.</summary>
public sealed class SpellTeleport(TileCoord to) : SpellEffect
{
    public TileCoord To { get; } = to;
}

/// <summary>
/// One spell: a row of content. It costs <see cref="Cost"/> from the inventory, needs
/// <see cref="Level"/> in the magic skill and gives <see cref="Xp"/> in it.
/// </summary>
public sealed class SpellDef
{
    /// <summary>The highest spell id.</summary>
    public const int MaxId = 0xFFFF;

    // F2: a row of content names its fields, and most take their defaults.
    public SpellDef(
        int id,
        string name,
        int level,
        SpellEffect effect,
        IReadOnlyList<ItemStack>? cost = null,
        int range = 8,
        int speed = 5,
        int xp = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id, MaxId);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, SkillDef.MaxLevel);
        if (range < 1 || range > CombatBonus.MaxRange || speed < 1 || xp < 0)
        {
            throw new ArgumentException("A spell has a range and a speed, and no negative xp.");
        }

        Cost = [.. cost ?? []];
        if (Cost.DistinctBy(s => s.Item).Count() != Cost.Count)
        {
            throw new ArgumentException("The cost repeats an item.", nameof(cost));
        }

        Id = id;
        Name = name;
        Level = level;
        Effect = effect;
        Range = range;
        Speed = speed;
        Xp = xp;
    }

    public int Id { get; }

    public string Name { get; }

    public int Level { get; }

    public SpellEffect Effect { get; }

    public IReadOnlyList<ItemStack> Cost { get; }

    /// <summary>Tiles from the caster to a creature it may be cast at.</summary>
    public int Range { get; }

    /// <summary>Ticks after a cast before the caster may swing, shoot or cast again.</summary>
    public int Speed { get; }

    public int Xp { get; }
}

/// <summary>
/// The spells a game has, and the skill they need. A typo in the skill or in
/// the item a spell costs fails when the game starts.
/// </summary>
public sealed class SpellSet
{
    private readonly Dictionary<int, SpellDef> _byId;

    public SpellSet(string skill, IReadOnlyList<SpellDef> spells, ItemSet items, SkillSet skills)
    {
        var def = skills.Find(skill) ?? throw new ArgumentException($"{skill} is unknown.", nameof(skill));
        Spells = [.. spells];
        _byId = Spells.DistinctBy(s => s.Id).ToDictionary(s => s.Id);
        if (_byId.Count != Spells.Count)
        {
            throw new ArgumentException("The spells repeat an id.", nameof(spells));
        }

        foreach (var spell in Spells)
        {
            if (spell.Level > def.LevelCap)
            {
                throw new ArgumentException(FormattableString.Invariant($"Spell {spell.Id} needs a level too high."), nameof(spells));
            }

            foreach (var stack in spell.Cost)
            {
                items.Require(stack.Item);
            }
        }

        Skill = skill;
    }

    /// <summary>The id of the skill that spells need.</summary>
    public string Skill { get; }

    public IReadOnlyList<SpellDef> Spells { get; }

    public SpellDef? Find(int id) => _byId.GetValueOrDefault(id);
}
