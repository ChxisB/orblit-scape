namespace Scape.Core;

/// <summary>The replicated component on a fighter.</summary>
public static class Health
{
    public const string Name = "Health";

    /// <summary>Ints in the component: the hit points now, then the most there can be.</summary>
    public const int Ints = 2;
}

/// <summary>A stat as a fight sees it: a skill level and the bonus worn gear adds.</summary>
public readonly record struct Rating(int Level, int Bonus);

/// <summary>How an attack is made. Each style beats one other and loses to the one that beats it.</summary>
public enum CombatStyle
{
    Melee,
    Ranged,
    Magic,
}

public static class CombatStyles
{
    extension(CombatStyle style)
    {
        /// <summary>The style this one has the edge over.</summary>
        public CombatStyle Beats => style switch
        {
            CombatStyle.Melee => CombatStyle.Magic,
            CombatStyle.Ranged => CombatStyle.Melee,
            CombatStyle.Magic => CombatStyle.Ranged,
            _ => throw new InvalidOperationException($"{style} is not a combat style."),
        };
    }
}

/// <summary>
/// What a worn item adds to a fight. A weapon also names how many ticks pass
/// between its swings. A ranged weapon names how far it shoots and the kind of
/// ammo it fires, and ammo names the kind it is.
/// </summary>
public sealed class CombatBonus
{
    /// <summary>
    /// The most tiles a shot or a spell may reach. A flight of that many tiles
    /// still fits the byte a projectile counts its ticks in.
    /// </summary>
    public const int MaxRange = 255;

    // F2: a row of content names its fields, and most take their defaults.
    public CombatBonus(
        int attack = 0,
        int strength = 0,
        int defence = 0,
        int? speed = null,
        CombatStyle style = CombatStyle.Melee,
        int? range = null,
        string? ammo = null)
    {
        if (attack < 0 || strength < 0 || defence < 0)
        {
            throw new ArgumentException("A bonus is not negative.");
        }

        if (speed < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "The speed is below one.");
        }

        CheckRanged(style, speed, range, ammo);
        Attack = attack;
        Strength = strength;
        Defence = defence;
        Speed = speed;
        Style = style;
        Range = range;
        Ammo = ammo;
    }

    public int Attack { get; }

    public int Strength { get; }

    public int Defence { get; }

    /// <summary>Ticks between swings, or null for an item that is not a weapon.</summary>
    public int? Speed { get; }

    /// <summary>Gear is melee or ranged. Magic comes from spells.</summary>
    public CombatStyle Style { get; }

    /// <summary>Tiles a ranged weapon shoots, or null for any other item.</summary>
    public int? Range { get; }

    /// <summary>The kind of ammo a ranged weapon fires, or the kind an arrow or bolt is.</summary>
    public string? Ammo { get; }

    private static void CheckRanged(CombatStyle style, int? speed, int? range, string? ammo)
    {
        if (style == CombatStyle.Magic)
        {
            throw new ArgumentException("Magic is not worn.", nameof(style));
        }

        if (style == CombatStyle.Ranged)
        {
            if (speed is null || range is not (>= 1 and <= MaxRange) || ammo is null)
            {
                throw new ArgumentException("A ranged weapon has a speed, a range and ammo.");
            }
        }
        else if (range is not null)
        {
            throw new ArgumentException("The range is on a weapon that is not ranged.", nameof(range));
        }
    }
}

/// <summary>How a creature fights. Its levels stand in for the skill and gear a player brings.</summary>
public sealed class NpcFight
{
    // F2: a row of content names its fields, and most take their defaults.
    public NpcFight(
        int health,
        int attack = 1,
        int strength = 1,
        int defence = 1,
        int speed = 4,
        int respawnTicks = 50,
        CombatStyle style = CombatStyle.Melee)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(health, 1);
        if (attack < 1 || strength < 1 || defence < 1)
        {
            throw new ArgumentException("A level is at least one.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(speed, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(respawnTicks, 1);
        Health = health;
        Attack = attack;
        Strength = strength;
        Defence = defence;
        Speed = speed;
        RespawnTicks = respawnTicks;
        Style = style;
    }

    public int Health { get; }

    public int Attack { get; }

    public int Strength { get; }

    public int Defence { get; }

    /// <summary>Ticks between swings.</summary>
    public int Speed { get; }

    /// <summary>Ticks from death to the creature standing at home again.</summary>
    public int RespawnTicks { get; }

    /// <summary>How it fights, and so which attacks have the edge over it.</summary>
    public CombatStyle Style { get; }
}

/// <summary>
/// The numbers a fight is worked out from. A roll is <c>(level + effortBase) *
/// (bonus + gearBase)</c>. The chance to hit is the attack roll over the sum of
/// both rolls, held between <see cref="MinHitChance"/> and <see cref="MaxHitChance"/>.
/// A hit does from one to the maximum, which is one plus the strength roll over
/// <see cref="HitDivisor"/>. An attack in a style that beats the defender's rolls
/// <see cref="StyleEdge"/> times as high, and one in a style that loses rolls that
/// much lower.
/// </summary>
public sealed class CombatRules
{
    // F2: a row of content names its fields, and most take their defaults.
    public CombatRules(
        string fightingSkill,
        string healthSkill,
        int effortBase = 8,
        int gearBase = 64,
        int hitDivisor = 320,
        double minHitChance = 0.05,
        double maxHitChance = 0.95,
        int baseHealth = 9,
        int unarmedSpeed = 4,
        int combatTicks = 8,
        int regenTicks = 100,
        int fightingXpPerHealth = 4,
        int healthXpPerHealth = 1,
        double styleEdge = 1.25,
        int projectileSpeed = 3)
    {
        ArgumentException.ThrowIfNullOrEmpty(fightingSkill);
        ArgumentException.ThrowIfNullOrEmpty(healthSkill);
        ArgumentOutOfRangeException.ThrowIfNegative(effortBase);
        ArgumentOutOfRangeException.ThrowIfLessThan(gearBase, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(hitDivisor, 1);

        // Written as comparisons, not ThrowIf calls, so a NaN passes as it always has.
        if (minHitChance <= 0 || minHitChance > maxHitChance || maxHitChance >= 1)
        {
            throw new ArgumentException("Hit chances must satisfy 0 < min <= max < 1.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(baseHealth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(unarmedSpeed, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(combatTicks);
        ArgumentOutOfRangeException.ThrowIfLessThan(regenTicks, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(fightingXpPerHealth);
        ArgumentOutOfRangeException.ThrowIfNegative(healthXpPerHealth);
        if (styleEdge < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(styleEdge), styleEdge, "A style edge is one or more.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(projectileSpeed, 1);
        FightingSkill = fightingSkill;
        HealthSkill = healthSkill;
        EffortBase = effortBase;
        GearBase = gearBase;
        HitDivisor = hitDivisor;
        MinHitChance = minHitChance;
        MaxHitChance = maxHitChance;
        BaseHealth = baseHealth;
        UnarmedSpeed = unarmedSpeed;
        CombatTicks = combatTicks;
        RegenTicks = regenTicks;
        FightingXpPerHealth = fightingXpPerHealth;
        HealthXpPerHealth = healthXpPerHealth;
        StyleEdge = styleEdge;
        ProjectileSpeed = projectileSpeed;
    }

    public string FightingSkill { get; }

    public string HealthSkill { get; }

    public int EffortBase { get; }

    public int GearBase { get; }

    public int HitDivisor { get; }

    public double MinHitChance { get; }

    public double MaxHitChance { get; }

    public int BaseHealth { get; }

    /// <summary>Ticks between swings with no weapon.</summary>
    public int UnarmedSpeed { get; }

    /// <summary>Ticks after a swing in which a fighter does not regenerate.</summary>
    public int CombatTicks { get; }

    /// <summary>Ticks between each hit point regained.</summary>
    public int RegenTicks { get; }

    /// <summary>Experience in the fighting skill for each hit point a creature had.</summary>
    public int FightingXpPerHealth { get; }

    /// <summary>Experience in the health skill for each hit point a creature had.</summary>
    public int HealthXpPerHealth { get; }

    /// <summary>How much a style that beats another multiplies the attack roll.</summary>
    public double StyleEdge { get; }

    /// <summary>Tiles a projectile crosses each tick.</summary>
    public int ProjectileSpeed { get; }

    /// <summary>The hit points a fighter with <paramref name="healthLevel"/> has when whole.</summary>
    public int MaxHealth(int healthLevel) => BaseHealth + healthLevel;

    /// <summary>Ticks a projectile flies over <paramref name="distance"/> tiles, at least one.</summary>
    public int FlightTicks(int distance) => Math.Max(1, (distance + ProjectileSpeed - 1) / ProjectileSpeed);

    /// <summary>What an attack in style <paramref name="from"/> multiplies its roll by against <paramref name="to"/>.</summary>
    public double Edge(CombatStyle from, CombatStyle to)
    {
        if (from.Beats == to)
        {
            return StyleEdge;
        }

        return to.Beats == from ? 1 / StyleEdge : 1;
    }

    /// <summary>The chance that a swing by <paramref name="attack"/> lands against <paramref name="defence"/>.</summary>
    public double HitChance(Rating attack, Rating defence, double edge = 1)
    {
        var attackRoll = Roll(attack) * edge;
        var chance = attackRoll / (attackRoll + Roll(defence));

        // Two zero rolls give a NaN, which hits at the upper limit. Math.Clamp would keep the NaN.
        return double.IsNaN(chance) ? MaxHitChance : Math.Clamp(chance, MinHitChance, MaxHitChance);
    }

    /// <summary>The most one hit by <paramref name="strength"/> does.</summary>
    public int MaxHit(Rating strength) => 1 + Roll(strength) / HitDivisor;

    /// <summary>The damage of one swing: zero for a miss.</summary>
    // F2: callers name the ratings at every call, so three of one type cannot swap.
    public int Swing(Random random, Rating attack, Rating strength, Rating defence, double edge = 1) =>
        Damage(random, MaxHit(strength), attack, defence, edge);

    /// <summary>The damage of one attack that does up to <paramref name="most"/>: zero for a miss.</summary>
    // F2: callers name the ratings at every call, so two of one type cannot swap.
    // F5: an attack answers with its damage and spends the dice it is given.
    public int Damage(Random random, int most, Rating attack, Rating defence, double edge = 1)
    {
        if (random.NextDouble() >= HitChance(attack, defence, edge))
        {
            return 0;
        }

        return 1 + random.Next(most);
    }

    private int Roll(Rating rating) => (rating.Level + EffortBase) * (rating.Bonus + GearBase);
}
