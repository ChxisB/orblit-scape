using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class RangedMagicPrayerTests : IAsyncDisposable
{
    private const int Bolts = 100;
    private const int Crossbow = 101;
    private const int Archer = 3;
    private const int Mage = 4;
    private const int Brute = 5;
    private const int WaitLimit = 40;

    /// <summary>The default items and a crossbow that fires bolts, which the bow cannot.</summary>
    private static readonly ItemSet ShotItems = new(
        [
            .. DefaultItems.All.Items,
            new ItemDef(Bolts, "Bolts", stackable: true, equipSlot: "ammo", combat: new CombatBonus(ammo: "bolt")),
            new ItemDef(
                Crossbow,
                "Crossbow",
                equipSlot: "weapon",
                combat: new CombatBonus(speed: 5, style: CombatStyle.Ranged, range: 7, ammo: "bolt")),
        ],
        DefaultSkills.All);

    private readonly ShotDice _dice = new();
    private TestServer _test;

    public RangedMagicPrayerTests() => _test = Start();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static long Id(PlacedNpc npc) => (long)npc.NetworkId;

    /// <summary>Shots that fly a tile a tick, so a long shot is in the air a long while.</summary>
    private static CombatRules SlowShots() => new(fightingSkill: "fighting", healthSkill: "health", projectileSpeed: 1);

    private static NpcSet Foes(int health = 30, int respawnTicks = 10) => new(
    [
        new NpcDef(1, "Rat", fight: new NpcFight(health, respawnTicks: respawnTicks)),
        new NpcDef(Archer, "Archer", fight: new NpcFight(health, style: CombatStyle.Ranged)),
        new NpcDef(Brute, "Brute", new Aggressive(sight: 6, leash: 12), new NpcFight(health, strength: 200)),
        new NpcDef(Mage, "Mage", fight: new NpcFight(health, style: CombatStyle.Magic)),
    ]);

    private TestServer Start(NpcSet? npcs = null, CombatRules? rules = null, PrayerSet? prayers = null) =>
        new(options => options with
        {
            Items = ShotItems,
            Npcs = npcs ?? Foes(),
            Combat = rules ?? options.Combat,
            Spells = DefaultSpells.All,
            Prayers = prayers ?? DefaultPrayers.All,
            Random = _dice,
        });

    /// <summary>Starts the server again with other creatures, rules or prayers.</summary>
    private async Task StartAsync(NpcSet? npcs = null, CombatRules? rules = null, PrayerSet? prayers = null)
    {
        await _test.DisposeAsync();
        _test = Start(npcs, rules, prayers);
    }

    private PlacedNpc Place(int npc, TileCoord tile) => Server.Npcs.Spawn(new NpcSpawn(npc, tile));

    private Container Worn(string player) => Server.Items.ContainerOf(player, ContainerId.Equipment)!;

    private Container Carried(string player) => Server.Items.ContainerOf(player, ContainerId.Inventory)!;

    private void Wear(string player, int item, int count = 1) => Worn(player).Add(item, count);

    private void Give(string player, int item, int count) => Carried(player).Add(item, count);

    private void Level(string player, string skill, int level) =>
        Server.Skills.Grant(player, skill, DefaultSkills.All.Curve.XpForLevel(level));

    /// <summary>Joins Ana with a shortbow and <paramref name="arrows"/> arrows worn.</summary>
    private async Task<TestClient> JoinBowmanAsync(int arrows = 10)
    {
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", ItemId.Shortbow);
        Wear("ana", ItemId.BronzeArrows, arrows);
        return ana;
    }

    /// <summary>
    /// Ticks until <paramref name="client"/> has <paramref name="count"/> of what
    /// <paramref name="of"/> counts, and returns the ticks that took.
    /// </summary>
    private async Task<int> TicksUntilAsync(TestClient client, int count, Func<TestClient, int> of)
    {
        var ticks = 0;
        do
        {
            await _test.TickAsync();
            Assert.True(++ticks <= WaitLimit, "Nothing arrived in time.");
        }
        while (of(client) < count);

        return ticks;
    }

    // A shot.

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task AShotLandsOnTheTickItsFlightGives(int distance)
    {
        var ana = await JoinBowmanAsync();
        var rat = Place(1, At(32 + distance, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        var fired = await TicksUntilAsync(ana, 1, client => client.Shots.Count);
        var landed = await TicksUntilAsync(ana, 1, client => client.Hits.Count);

        var expected = DefaultCombat.Rules.FlightTicks(distance);
        Assert.Equal(expected, ana.Shots[0].Ticks);
        Assert.Equal(expected, landed);
        Assert.True(fired > 0);
        Assert.True(Server.Combat.HealthOfNpc(rat)?.Now < 30);
    }

    [Fact]
    public async Task AShotFromBeyondTheRangeClosesInFirst()
    {
        var ana = await JoinBowmanAsync();
        var rat = Place(1, At(44, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await TicksUntilAsync(ana, 1, client => client.Shots.Count);

        var from = Server.Movement.TileOf("ana")!.Value;
        Assert.True(44 - from.X <= 7);
        Assert.True(from.X > 32);
    }

    [Fact]
    public async Task AShotSpendsOneArrowEachAndStopsWithNoneLeft()
    {
        var ana = await JoinBowmanAsync(arrows: 2);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(40);

        Assert.Equal(2, ana.Shots.Count);
        Assert.Equal(0, Worn("ana").CountOf(ItemId.BronzeArrows));
        Assert.Equal(["You have no ammo."], ana.Notices);
    }

    [Fact]
    public async Task AShotIsNotFiredByABowWithNoArrows()
    {
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", ItemId.Shortbow);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(8);

        Assert.Empty(ana.Shots);
        Assert.Equal(["You have no ammo."], ana.Notices);
    }

    [Fact]
    public async Task AShotIsNotFiredWithAmmoOfAnotherKind()
    {
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", ItemId.Shortbow);
        Wear("ana", Bolts, 5);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(8);

        Assert.Empty(ana.Shots);
        Assert.Equal(5, Worn("ana").CountOf(Bolts));
    }

    [Fact]
    public async Task AShotWhoseTargetDiesOrIsReplacedInFlightDoesNoHarm()
    {
        // A slow shot flies 7 ticks, so the second leaves before the first lands.
        await StartAsync(Foes(health: 1, respawnTicks: 1), SlowShots());
        var ana = await JoinBowmanAsync();
        var rat = Place(1, At(39, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(30);

        Assert.Equal(2, ana.Shots.Count);
        Assert.Single(ana.Hits);
        Assert.Equal(1, Server.Combat.HealthOfNpc(rat)?.Now);
    }

    [Fact]
    public async Task AShotWhoseShooterDiesInTheMeantimeLandsOnNobody()
    {
        await StartAsync(rules: SlowShots());
        var ana = await JoinBowmanAsync();
        var rat = Place(1, At(39, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await TicksUntilAsync(ana, 1, client => client.Shots.Count);

        Place(Brute, At(33, 32));
        await _test.TickAsync(12);

        Assert.Contains("Oh dear, you are dead.", ana.Notices);
        Assert.Equal(30, Server.Combat.HealthOfNpc(rat)?.Now);
    }

    [Fact]
    public async Task AShotCountsTheArrowsAsGearOnlyForTheBowTheySuit()
    {
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", ItemId.BronzeArrows, 10);
        Wear("ana", ItemId.BronzeSword);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await TicksUntilAsync(ana, 1, client => client.FoeHits.Count);

        // The sword adds 6 strength and gives a most of 1 + 9 * 70 / 320. The
        // arrows would add 4 and make it 3.
        Assert.Equal(1 + ((1 + 8) * (6 + 64) / 320), ana.FoeHits[0].Damage);
    }

    // The style edge.

    [Theory]
    [InlineData(1, true)]
    [InlineData(Archer, false)]
    [InlineData(Mage, false)]
    public async Task TheStyleEdgeLetsABowHurtOnlyACreatureItBeats(int foe, bool lands)
    {
        _dice.Hit = 0.55;
        var ana = await JoinBowmanAsync();
        var target = Place(foe, At(34, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(target));
        await TicksUntilAsync(ana, 1, client => client.Hits.Count);

        Assert.Equal(lands, ana.Hits[0].Damage > 0);
    }

    [Theory]
    [InlineData(Mage, true)]
    [InlineData(1, false)]
    public async Task TheStyleEdgeLetsACreatureHurtABowmanOnlyIfItBeatsTheBow(int foe, bool lands)
    {
        _dice.Hit = 0.48;
        var ana = await JoinBowmanAsync();
        var target = Place(foe, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(target));
        await _test.TickAsync(10);

        var onAna = ana.Hits.Where(hit => hit.Target == ana.Avatar).ToList();
        Assert.NotEmpty(onAna);
        Assert.Equal(lands, onAna[0].Damage > 0);
    }

    // A spell.

    [Fact]
    public async Task ASpellCostsRunesFliesHurtsAndGivesExperience()
    {
        var ana = await _test.JoinAsync("Ana");
        Give("ana", ItemId.Runes, 5);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);

        await ana.CastAsync(SpellId.Strike, Id(rat));
        await TicksUntilAsync(ana, 1, client => client.Shots.Count);
        var landed = await TicksUntilAsync(ana, 1, client => client.Hits.Count);

        Assert.Equal(CombatStyle.Magic, Assert.Single(ana.Shots).Style);
        Assert.Equal(DefaultCombat.Rules.FlightTicks(3), landed);
        Assert.True(Server.Combat.HealthOfNpc(rat)?.Now < 30);
        Assert.Equal(4, Carried("ana").CountOf(ItemId.Runes));
        Assert.Equal(6, Server.Skills.XpOf("ana", "magic"));
    }

    [Fact]
    public async Task ASpellThatIsRefusedCostsNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(35, 32));
        var far = Place(1, At(55, 32));
        await _test.TickAsync(2);
        await ana.CastAsync(SpellId.Strike, Id(rat));
        await _test.TickAsync();
        Give("ana", ItemId.Runes, 5);
        await ana.CastAsync(SpellId.Strike, Id(far));
        await ana.CastAsync(SpellId.Daze, Id(rat));
        await _test.TickAsync();

        Assert.Equal(["You do not have what that spell costs.", "That is out of range.", "You need level 5 in magic."], ana.Notices);
        Assert.Equal(5, Carried("ana").CountOf(ItemId.Runes));
        Assert.Equal(0, Server.Skills.XpOf("ana", "magic"));
        Assert.Empty(ana.Shots);
    }

    [Fact]
    public async Task ASpellWaitsForTheOneBeforeIt()
    {
        var ana = await _test.JoinAsync("Ana");
        Give("ana", ItemId.Runes, 5);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);

        await ana.CastAsync(SpellId.Strike, Id(rat));
        await ana.CastAsync(SpellId.Strike, Id(rat));
        await _test.TickAsync();

        Assert.Equal(["You are not ready yet."], ana.Notices);
        Assert.Single(ana.Shots);
    }

    [Fact]
    public async Task ASpellIsReadyAgainOnTheTickItsSpeedRunsOut()
    {
        var ana = await _test.JoinAsync("Ana");
        Give("ana", ItemId.Runes, 5);
        var rat = Place(1, At(35, 32));
        await _test.TickAsync(2);
        var speed = DefaultSpells.All.Find(SpellId.Strike)!.Speed; // Strike is in the default spells.

        await ana.CastAsync(SpellId.Strike, Id(rat));
        await _test.TickAsync(speed - 1);
        await ana.CastAsync(SpellId.Strike, Id(rat));
        await _test.TickAsync();
        Assert.Equal(["You are not ready yet."], ana.Notices);

        await ana.CastAsync(SpellId.Strike, Id(rat));
        await _test.TickAsync();

        Assert.Equal(2, ana.Shots.Count);
    }

    [Fact]
    public async Task ASpellOfStunningHoldsACreatureBack()
    {
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "magic", 5);
        Give("ana", ItemId.Runes, 5);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.CastAsync(SpellId.Daze, Id(rat));
        await _test.TickAsync(5);
        var held = ana.Hits.Count(hit => hit.Target == ana.Avatar);
        await _test.TickAsync(3);
        var freed = ana.Hits.Count(hit => hit.Target == ana.Avatar);

        Assert.Equal(0, held);
        Assert.True(freed > 0);
    }

    [Fact]
    public async Task ASpellOfTeleportingTakesTheCasterHomeAndEndsTheirAttack()
    {
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "magic", 10);
        Give("ana", ItemId.Runes, 5);
        var rat = Place(1, At(45, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(3);
        Assert.True(Server.Movement.TileOf("ana")?.X > 32);

        await ana.CastAsync(SpellId.Home);
        await ana.CastAsync(SpellId.Home);
        await _test.TickAsync(12);

        Assert.Equal(OpenWorld.SpawnTile, Server.Movement.TileOf("ana"));
        Assert.Equal(["You are not ready yet."], ana.Notices);
        Assert.Equal(2, Carried("ana").CountOf(ItemId.Runes));
    }

    // A prayer.

    [Fact]
    public async Task APrayerDrainsAPointInTwentyTicksAndGoesOffAtZero()
    {
        var ana = await _test.JoinAsync("Ana");
        Assert.Equal(1, ana.Prayer.Points);

        await ana.SendAsync(new TogglePrayer(PrayerId.ThickSkin));
        await _test.TickAsync(19);
        Assert.Equal([PrayerId.ThickSkin], ana.Prayer.Active);
        Assert.Equal(1, ana.Prayer.Points);
        Assert.Equal(10, Server.Prayers!.PercentOf("ana", CombatStat.Defence));

        await _test.TickAsync();
        Assert.Equal(0, ana.Prayer.Points);
        Assert.Empty(ana.Prayer.Active);
        Assert.Equal(0, Server.Prayers.PercentOf("ana", CombatStat.Defence));
        Assert.Equal(["You have run out of prayer points."], ana.Notices);
    }

    [Fact]
    public async Task APrayerTellsThePlayerWhenTheirPrayerLevelChanges()
    {
        var ana = await _test.JoinAsync("Ana");
        Assert.Equal(1, ana.Prayer.Max);

        Level("ana", "prayer", 20);
        await _test.TickAsync();

        Assert.Equal(20, ana.Prayer.Max);
        Assert.Equal(20, ana.Prayer.Points);
    }

    [Fact]
    public async Task APrayerCannotBeSwitchedOnWithNoPointsOrTooLowALevel()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(new TogglePrayer(PrayerId.BurstOfStrength));
        await _test.TickAsync();
        Assert.Equal(["You need level 4 in prayer."], ana.Notices);

        await ana.SendAsync(new TogglePrayer(PrayerId.ThickSkin));
        await _test.TickAsync(20);
        await ana.SendAsync(new TogglePrayer(PrayerId.ThickSkin));
        await _test.TickAsync();

        Assert.Equal("You have no prayer points.", ana.Notices[^1]);
        Assert.Empty(ana.Prayer.Active);
    }

    [Fact]
    public async Task APrayerReplacesAnotherThatRaisesTheSameStat()
    {
        var prayers = new PrayerSet(
            "prayer",
            [
                new PrayerDef(1, "Small", level: 1, CombatStat.Defence, percent: 5, drain: 5),
                new PrayerDef(2, "Big", level: 1, CombatStat.Defence, percent: 10, drain: 8),
            ],
            DefaultSkills.All);
        await StartAsync(prayers: prayers);
        var ana = await _test.JoinAsync("Ana");

        await ana.SendAsync(new TogglePrayer(1));
        await ana.SendAsync(new TogglePrayer(2));
        await _test.TickAsync();
        Assert.Equal([2], ana.Prayer.Active);

        await ana.SendAsync(new TogglePrayer(2));
        await _test.TickAsync();
        Assert.Empty(ana.Prayer.Active);
    }

    [Fact]
    public async Task APrayerRaisesTheStrengthASwingIsRolledWithWhileItIsOn()
    {
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "fighting", 15);
        Level("ana", "prayer", 4);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await TicksUntilAsync(ana, 1, client => client.FoeHits.Count);
        await ana.SendAsync(new TogglePrayer(PrayerId.BurstOfStrength));
        await TicksUntilAsync(ana, 2, client => client.FoeHits.Count);

        // Level 15 rolls (15 + 8) * 64 / 320. Ten percent is a level and a
        // half, which rounds up to 17. Cutting it off at 16 would change
        // nothing.
        Assert.Equal([1 + (23 * 64 / 320), 1 + (25 * 64 / 320)], ana.FoeHits.Select(hit => hit.Damage));
    }

    /// <summary>
    /// Dice for a fight. Every hit roll answers <see cref="Hit"/>. A damage roll answers its top
    /// value, so a hit does the most it can.
    /// </summary>
    private sealed class ShotDice : Random
    {
        public double Hit { get; set; }

        public override double NextDouble() => Hit;

        public override int Next(int maxValue) => maxValue - 1;
    }
}

file static class ShotReading
{
    extension(TestClient client)
    {
        /// <summary>The hits that landed on something other than this player.</summary>
        public IReadOnlyList<Hit> FoeHits => [.. client.Hits.Where(hit => hit.Target != client.Avatar)];

        public IReadOnlyList<Projectile> Shots => [.. client.Messages.OfType<Projectile>()];

        /// <summary>The last prayer view the player was sent. Every player is sent one when they join.</summary>
        public PrayerView Prayer => client.Messages.OfType<PrayerView>().Last();

        public Task CastAsync(int spell, long target = 0) => client.SendAsync(new Cast(spell, target));
    }
}
