using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class CombatTests
{
    private static readonly Rating Even = new(20, 10);

    private static CombatRules Rules() => new("fighting", "health");

    // CombatRules.

    [Fact]
    public void AnEvenMatchLandsHalfTheTime()
    {
        Assert.Equal(0.5, Rules().HitChance(Even, Even));
    }

    [Fact]
    public void AStrongerAttackerLandsMoreOftenThanAWeakerOne()
    {
        var strong = new Rating(40, 30);
        var weak = new Rating(5, 0);

        Assert.True(Rules().HitChance(strong, weak) > 0.5);
        Assert.True(Rules().HitChance(weak, strong) < 0.5);
    }

    [Fact]
    public void AChanceIsHeldBetweenTheFloorAndTheCeiling()
    {
        var huge = new Rating(1000, 1000);
        var none = new Rating(0, 0);

        Assert.Equal(0.95, Rules().HitChance(huge, none));
        Assert.Equal(0.05, Rules().HitChance(none, huge));
    }

    [Fact]
    public void ManySeededSwingsHitAtTheRateTheFormulaGives()
    {
        var attack = new Rating(30, 20);
        var defence = new Rating(25, 5);
        var expected = Rules().HitChance(attack, defence);
        var random = new Random(7);
        var hits = 0;
        for (var i = 0; i < 10000; i++)
        {
            if (Rules().Swing(random, attack, attack, defence) > 0)
            {
                hits++;
            }
        }

        Assert.Equal(expected, hits / 10000.0, 0.01);
    }

    [Fact]
    public void AHitDoesFromOneUpToTheMaximumAndNoMore()
    {
        var strength = new Rating(40, 30);
        var most = Rules().MaxHit(strength);
        var random = new Random(3);
        var damages = new HashSet<int>();
        for (var i = 0; i < 5000; i++)
        {
            damages.Add(Rules().Swing(random, new Rating(99, 99), strength, new Rating(1, 0)));
        }

        damages.Remove(0);

        Assert.Equal(most, damages.Max());
        Assert.Equal(1, damages.Min());
    }

    [Fact]
    public void TheMaximumHitRisesWithLevelAndWithGear()
    {
        Assert.Equal(2, Rules().MaxHit(new Rating(1, 0)));
        Assert.Equal(12, Rules().MaxHit(new Rating(50, 0)));
        Assert.Equal(21, Rules().MaxHit(new Rating(50, 50)));
    }

    // C# only: the roll passes an int here, and wrapped it would turn the best gear into the worst.
    [Fact]
    public void AHugeBonusRollsHighAndIsNotWrapped()
    {
        var huge = new Rating(SkillDef.MaxLevel, 25_000_000);

        Assert.Equal(0.95, Rules().HitChance(huge, new Rating(1, 0)));
        Assert.Equal(10_000_026, Rules().MaxHit(huge));
    }

    [Fact]
    public void WholeHealthIsTheBasePlusTheHealthLevel()
    {
        Assert.Equal(10, Rules().MaxHealth(1));
        Assert.Equal(59, Rules().MaxHealth(50));
    }

    [Fact]
    public void RejectsNumbersThatCannotMakeAFight()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("", "health"));
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", hitDivisor: 0));
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", minHitChance: 0));
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", minHitChance: 0.9, maxHitChance: 0.5));
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", maxHitChance: 1));
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", regenTicks: 0));
    }

    // C# only: a value outside the enum has no style it beats.
    [Fact]
    public void AStyleOutsideTheEnumBeatsNothing()
    {
        Assert.Throws<InvalidOperationException>(() => ((CombatStyle)9).Beats);
    }

    // CombatBonus.

    [Fact]
    public void HasNoSpeedUnlessItIsGivenOne()
    {
        Assert.Null(new CombatBonus(attack: 3).Speed);
        Assert.Equal(4, new CombatBonus(speed: 4).Speed);
    }

    [Fact]
    public void RejectsANegativeBonusAndASpeedBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(attack: -1));
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(strength: -1));
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(defence: -1));
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(speed: 0));
    }

    // NpcFight.

    [Fact]
    public void RejectsHealthOrALevelBelowOneAndASpeedBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(0));
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(1, attack: 0));
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(1, strength: 0));
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(1, defence: 0));
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(1, speed: 0));
        Assert.ThrowsAny<ArgumentException>(() => new NpcFight(1, respawnTicks: 0));
        Assert.Equal(1, new NpcFight(1).Health);
    }

    [Fact]
    public void ACreatureHasNoFightUnlessItIsGivenOne()
    {
        Assert.Null(new NpcDef(1, "Guard").Fight);
        Assert.Equal(2, new NpcDef(1, "Rat", fight: new NpcFight(2)).Fight!.Health);
    }

    // ItemDef combat.

    [Fact]
    public void OnlyAWornItemHasABonusAndOnlyAWeaponHasASpeed()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "Rock", combat: new CombatBonus(attack: 1)));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "Cap", equipSlot: "head", combat: new CombatBonus(speed: 4)));
        Assert.Equal(2, new ItemDef(1, "Cap", equipSlot: "head", combat: new CombatBonus(defence: 2)).Combat!.Defence);
        Assert.Equal(4, new ItemDef(1, "Sword", equipSlot: "weapon", combat: new CombatBonus(attack: 2, speed: 4)).Combat!.Speed);
    }

    // Hit.

    [Fact]
    public void AHitSurvivesTheWireWithTheLargestValuesItCanHold()
    {
        var back = Heard(new Hit(0xFFFFFFFF, 0xFFFF));

        Assert.Equal(0xFFFFFFFFL, back.Target);
        Assert.Equal(0xFFFF, back.Damage);
        Assert.Equal(0, Heard(new Hit(5, 0)).Damage);
    }

    [Fact]
    public void AHitRejectsATargetOrADamageThatDoesNotFit()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Hit(-1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Hit(0x100000000, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Hit(0, -1));
        Assert.ThrowsAny<ArgumentException>(() => new Hit(0, 0x10000));
    }

    [Fact]
    public void AHitOfTheWrongLengthIsMalformed()
    {
        var bytes = MessageCodec.Encode(new Hit(1, 1));

        Assert.Equal(BadMessage.Malformed, WhyServer(bytes[..6]));
        Assert.Equal(BadMessage.TooLarge, WhyServer([.. bytes, 0]));
    }
}
