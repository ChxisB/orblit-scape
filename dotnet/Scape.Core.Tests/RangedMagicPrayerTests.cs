using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class RangedMagicPrayerTests
{
    private static CombatRules Rules() => new("fighting", "health");

    private static SkillSet MagicSkills() => new(new XpCurve(30, 4), [new SkillDef("magic", "Magic", 10)]);

    private static SpellSet Spells(IReadOnlyList<SpellDef> spells) => new("magic", spells, ItemSupport.Items(), MagicSkills());

    private static SpellDef Stun(int id, int level = 1, IReadOnlyList<ItemStack>? cost = null) =>
        new(id, $"S{id}", level, new SpellStun(3), cost);

    private static PrayerDef Prayer(int id, int level = 1, int drain = 5) =>
        new(id, $"P{id}", level, CombatStat.Attack, 10, drain);

    // Flight.

    [Fact]
    public void TakesATickForEachThreeTilesAndAtLeastOne()
    {
        int[] flight = [.. Enumerable.Range(0, 11).Select(Rules().FlightTicks)];

        Assert.Equal([1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4], flight);
    }

    [Fact]
    public void AShotThatMovesATileATickTakesTheDistanceInTicks()
    {
        var slow = new CombatRules("fighting", "health", projectileSpeed: 1);

        Assert.Equal([1, 2, 3, 4], Enumerable.Range(1, 4).Select(slow.FlightTicks));
    }

    // Style edge.

    [Fact]
    public void EachStyleBeatsOneAndLosesToOne()
    {
        var rules = Rules();

        Assert.Equal(1.25, rules.Edge(CombatStyle.Ranged, CombatStyle.Melee));
        Assert.Equal(1.25, rules.Edge(CombatStyle.Melee, CombatStyle.Magic));
        Assert.Equal(1.25, rules.Edge(CombatStyle.Magic, CombatStyle.Ranged));
        Assert.Equal(1 / 1.25, rules.Edge(CombatStyle.Melee, CombatStyle.Ranged));
        Assert.Equal(1, rules.Edge(CombatStyle.Melee, CombatStyle.Melee));
    }

    [Fact]
    public void AnEdgeAboveOneRaisesAChanceToHit()
    {
        var even = new Rating(20, 10);

        Assert.True(Rules().HitChance(even, even, 1.25) > 0.5);
        Assert.True(Rules().HitChance(even, even, 1 / 1.25) < 0.5);
    }

    [Fact]
    public void AnEdgeIsAtLeastOneSoItNeverFavoursTheLoser()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CombatRules("fighting", "health", styleEdge: 0.9));
    }

    // Gear.

    [Fact]
    public void ARangedWeaponHasARangeAndAnAmmoKind()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(style: CombatStyle.Ranged));
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(style: CombatStyle.Ranged, speed: 4, range: 0, ammo: "arrow"));

        // A bow with no speed would never be ready again.
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(style: CombatStyle.Ranged, range: 7, ammo: "arrow"));

        // A melee weapon does not shoot.
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(range: 5, speed: 4));
        Assert.ThrowsAny<ArgumentException>(() => new CombatBonus(style: CombatStyle.Magic));
    }

    [Fact]
    public void ABowGoesInTheWeaponSlotAndAnArrowInTheAmmoSlot()
    {
        var bow = new CombatBonus(style: CombatStyle.Ranged, range: 7, ammo: "arrow", speed: 5);
        var arrow = new CombatBonus(strength: 7, ammo: "arrow");

        Assert.Equal("ammo", ItemDef.EquipSlots[^1]);
        Assert.Same(bow, new ItemDef(1, "Bow", equipSlot: "weapon", combat: bow).Combat);
        Assert.Same(arrow, new ItemDef(2, "Arrow", stackable: true, equipSlot: "ammo", combat: arrow).Combat);
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(1, "Bow", equipSlot: "body", combat: bow));
        Assert.ThrowsAny<ArgumentException>(() => new ItemDef(2, "Arrow", equipSlot: "head", combat: arrow));
    }

    [Fact]
    public void ACreatureFightsInMeleeUnlessItSaysOtherwise()
    {
        Assert.Equal(CombatStyle.Melee, new NpcFight(3).Style);
    }

    [Fact]
    public void ASaveFromBeforeTheAmmoSlotStillReads()
    {
        var state = PlayerState.Decode("""{"version":2,"tile":[1,1,0],"containers":{"equipment":[[3,9,1]]}}"""u8);

        Assert.Equal(11, state.Containers[ContainerId.Equipment].Count);
    }

    // Spells.

    [Fact]
    public void SpellsAreFoundById()
    {
        var spell = new SpellDef(1, "Bolt", 1, new SpellDamage(5));

        Assert.Same(spell, Spells([spell]).Find(1));
        Assert.Null(Spells([spell]).Find(2));
    }

    [Fact]
    public void SpellsAreCheckedAgainstTheGameWhenItStarts()
    {
        Assert.ThrowsAny<ArgumentException>(() => Spells([Stun(1), Stun(1)]));
        Assert.ThrowsAny<ArgumentException>(() => Spells([Stun(1, level: 11)]));
        Assert.ThrowsAny<ArgumentException>(() => Spells([Stun(1, cost: [new ItemStack(999, 1)])]));
        Assert.ThrowsAny<ArgumentException>(() => new SpellSet("nope", [], ItemSupport.Items(), MagicSkills()));
    }

    [Fact]
    public void SpellsCostEachItemOnceAndHaveARealEffect()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SpellDef(1, "Bolt", 1, new SpellDamage(5), [new ItemStack(2, 1), new ItemStack(2, 2)]));
        Assert.ThrowsAny<ArgumentException>(() => new SpellDamage(0));
        Assert.ThrowsAny<ArgumentException>(() => new SpellStun(0));
    }

    // Prayers.

    [Fact]
    public void PrayersAreCheckedAgainstTheGameWhenItStarts()
    {
        Assert.NotNull(new PrayerSet("magic", [Prayer(1)], MagicSkills()).Find(1));
        Assert.ThrowsAny<ArgumentException>(() => new PrayerSet("magic", [Prayer(1), Prayer(1)], MagicSkills()));
        Assert.ThrowsAny<ArgumentException>(() => new PrayerSet("magic", [Prayer(1, level: 11)], MagicSkills()));
        Assert.ThrowsAny<ArgumentException>(() => Prayer(1, drain: 0));
        Assert.ThrowsAny<ArgumentException>(() => new PrayerSet("x", [], MagicSkills()));
    }

    // Messages.

    [Fact]
    public void ACastNamesASpellAndATarget()
    {
        var cast = RoundTrip(new Cast(513, 70000));

        Assert.Equal((513, 70000L), (cast.Spell, cast.Target));
        Assert.ThrowsAny<ArgumentException>(() => new Cast(0x10000, 0));
        Assert.Equal(BadMessage.Malformed, WhyClient([16, 1, 0]));
    }

    [Fact]
    public void APrayerToggleNamesAPrayer()
    {
        Assert.Equal(7, RoundTrip(new TogglePrayer(7)).Prayer);
        Assert.ThrowsAny<ArgumentException>(() => new TogglePrayer(0));
        Assert.Equal(BadMessage.Malformed, WhyClient([17, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([17]));
    }

    [Fact]
    public void AProjectileKeepsItsEndsItsFlightAndItsStyle()
    {
        var heard = Heard(new Projectile(9, 70000, 3, CombatStyle.Magic));

        Assert.Equal((9L, 70000L, 3), (heard.From, heard.To, heard.Ticks));
        Assert.Equal(CombatStyle.Magic, heard.Style);
        Assert.ThrowsAny<ArgumentException>(() => new Projectile(1, 2, 0, CombatStyle.Ranged));
        Assert.Equal(BadMessage.Malformed, WhyServer([15, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3]));
    }

    // C# only: a cast can make a style that Dart's enum cannot hold.
    [Fact]
    public void AProjectileCannotBeBuiltWithAStyleOutsideTheEnum()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Projectile(1, 2, 1, (CombatStyle)9));
    }

    [Fact]
    public void APrayerViewKeepsThePointsAndThePrayersThatAreOn()
    {
        var heard = Heard(new PrayerView(12, 30, [2, 5]));

        Assert.Equal((12, 30), (heard.Points, heard.Max));
        Assert.Equal([2, 5], heard.Active);
        Assert.ThrowsAny<ArgumentException>(() => new PrayerView(31, 30, []));
        Assert.ThrowsAny<ArgumentException>(() => new PrayerView(1, 30, [1, 2, 3, 4]));
        Assert.ThrowsAny<ArgumentException>(() => new PrayerView(1, 30, [2, 2]));
        Assert.Equal(BadMessage.Malformed, WhyServer([16, 1, 0, 30, 0, 2, 4]));
        Assert.Equal(BadMessage.Malformed, WhyServer([16, 31, 0, 30, 0, 0]));
    }
}
