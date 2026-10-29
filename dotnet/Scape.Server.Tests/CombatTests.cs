using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class CombatTests : IAsyncDisposable
{
    private const int Mace = 100;
    private const int Plate = 101;
    private const int Club = 102;

    /// <summary>The default items and three with bonuses far past anything in the content, so a test can tell that gear counts.</summary>
    private static readonly ItemSet GearItems = new(
        [
            .. DefaultItems.All.Items,
            new ItemDef(Mace, "Mace", equipSlot: "weapon", combat: new CombatBonus(attack: 300, strength: 600, speed: 4)),
            new ItemDef(Plate, "Plate", equipSlot: "body", combat: new CombatBonus(defence: 600)),
            new ItemDef(Club, "Club", equipSlot: "weapon", combat: new CombatBonus(speed: 10)),
        ],
        DefaultSkills.All);

    private TestServer _test = Start();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static long Id(PlacedNpc npc) => (long)npc.NetworkId;

    /// <summary>Every swing lands for one.</summary>
    private static FightDice AlwaysOne() => new FightDice(0);

    /// <summary>Every swing misses.</summary>
    private static FightDice NeverHits() => new FightDice(0.99);

    /// <summary>A rat that is easy to read: one hit point per health, swinging every <paramref name="speed"/> ticks.</summary>
    private static NpcFight Rat(int health = 3, int speed = 4, int defence = 1, int respawnTicks = 10) =>
        new(health, defence: defence, speed: speed, respawnTicks: respawnTicks);

    /// <summary>A rat that fights as <paramref name="fight"/> says, and a post that does not fight.</summary>
    private static NpcSet Foes(NpcFight? fight = null, Behaviour? behaviour = null) =>
        new([new NpcDef(1, "Rat", behaviour ?? new Idle(), fight ?? Rat()), new NpcDef(2, "Post")]);

    private static TestServer Start(NpcSet? npcs = null, Random? random = null, CombatRules? rules = null) =>
        new(options => options with
        {
            Items = GearItems,
            Npcs = npcs ?? Foes(),
            Combat = rules ?? options.Combat,
            Random = random ?? AlwaysOne(),
        });

    /// <summary>Starts the server again with other creatures or other dice.</summary>
    private async Task StartAsync(NpcSet? npcs = null, Random? random = null)
    {
        await _test.DisposeAsync();
        _test = Start(npcs, random);
    }

    private PlacedNpc Place(int npc, TileCoord tile, int radius = 0) => Server.Npcs.Spawn(new NpcSpawn(npc, tile, radius));

    private void Wear(string player, int item) => Server.Items.ContainerOf(player, ContainerId.Equipment)!.Add(item, 1);

    // Attacking.

    [Fact]
    public async Task AClickWalksUpToTheCreatureAndSwings()
    {
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(36, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(8);

        Assert.NotEmpty(ana.Hits);
        Assert.Equal(Id(rat), ana.Hits[0].Target);
        Assert.True(Server.Combat.HealthOfNpc(rat)?.Now < 3);
    }

    [Fact]
    public async Task ACreatureWithNoFightCannotBeAttacked()
    {
        var ana = await _test.JoinAsync("Ana");
        var post = Place(2, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(post));
        await _test.TickAsync(4);

        Assert.Equal(["You cannot attack that."], ana.Notices);
        Assert.Empty(ana.Hits);
    }

    [Fact]
    public async Task IsAFightForTheCombatTicksAfterASwing()
    {
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        Assert.False(Server.Combat.IsFighting("ana"));

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(2);
        Assert.True(Server.Combat.IsFighting("ana"));

        await _test.TickAsync(40);
        Assert.False(Server.Combat.IsFighting("ana"));
        Assert.False(Server.Combat.IsFighting("nobody"));
    }

    [Fact]
    public async Task APlayerWhoDropsInAFightStaysInTheWorldLonger()
    {
        await StartAsync(random: NeverHits());
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(2);

        await _test.LeaveAsync(ana);
        await _test.TickAsync(30);
        Assert.True(Server.Combat.IsFighting("ana"));
        Assert.NotNull(Server.Movement.TileOf("ana"));

        await _test.TickAsync(80);
        Assert.Null(Server.Movement.TileOf("ana"));
    }

    [Fact]
    public async Task AClickOnNothingDoesNothing()
    {
        var ana = await _test.JoinAsync("Ana");

        await ana.AttackAsync(9999);
        await _test.TickAsync(4);

        Assert.Empty(ana.Hits);
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task SwingsComeAtTheWeaponSpeed()
    {
        await StartAsync(random: NeverHits());
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);

        // The first swing lands on the click tick, then one every four ticks.
        Assert.InRange(ana.Hits.Count(hit => hit.Target == Id(rat)), 5, 6);
    }

    [Fact]
    public async Task ASwordSwingsFasterThanBareHands()
    {
        await StartAsync(random: NeverHits());
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", ItemId.BronzeSword);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);

        var swings = ana.Hits.Count(hit => hit.Target == Id(rat));
        var speed = DefaultItems.All.Require(ItemId.BronzeSword).Combat!.Speed!.Value;
        Assert.True(speed < DefaultCombat.Rules.UnarmedSpeed + 1);
        Assert.InRange(swings, 20 / speed, (20 / speed) + 2);
    }

    // The sword and bare hands share a speed, so the test above cannot tell a weapon's speed from none.
    [Fact]
    public async Task ASlowWeaponSwingsAtItsOwnSpeed()
    {
        await StartAsync(random: NeverHits());
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", Club);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);

        Assert.InRange(ana.Hits.Count(hit => hit.Target == Id(rat)), 2, 3);
    }

    [Fact]
    public async Task AWeaponAttackBonusTurnsAMissIntoAHit()
    {
        // Bare hands have an even chance, which 0.52 misses. The mace beats it.
        await StartAsync(random: new FightDice(0.52));
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", Mace);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync();

        Assert.True(ana.Hits[0].Damage > 0);
    }

    [Fact]
    public async Task BareHandsMissTheSameRoll()
    {
        await StartAsync(random: new FightDice(0.52));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync();

        Assert.Equal(0, ana.Hits[0].Damage);
    }

    [Fact]
    public async Task StrengthBonusRaisesTheMostAHitDoes()
    {
        await StartAsync(random: new FightDice(0, damage: 99));
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", Mace);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync();

        Assert.Equal(DefaultCombat.Rules.MaxHit(new Rating(1, 600)), ana.Hits[0].Damage);
    }

    [Fact]
    public async Task ACreatureWithAHighDefenceIsMissedMore()
    {
        // 0.3 beats an even chance but not one against defence 50.
        await StartAsync(Foes(Rat(health: 50, defence: 50)), new FightDice(0.3));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(12);

        var swings = ana.Hits.Where(hit => hit.Target == Id(rat)).ToList();
        Assert.NotEmpty(swings);
        Assert.All(swings, hit => Assert.Equal(0, hit.Damage));
    }

    [Fact]
    public async Task AnAttackFollowsACreatureThatMoves()
    {
        // A wandering rat leaves the tile the click walked towards.
        await StartAsync(Foes(Rat(health: 50), new Wander()), new FightDice(0));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(36, 32), radius: 6);
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(40);

        Assert.Contains(ana.Hits, hit => hit.Target == Id(rat));
        Assert.DoesNotContain("You cannot reach that.", ana.Notices);
    }

    // Winning.

    [Fact]
    public async Task TheCreatureGoesAndTheKillHandlerRunsOnce()
    {
        await StartAsync(Foes(Rat(respawnTicks: 500)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        var kills = new List<(string, PlacedNpc)>();
        Server.Combat.AddKillHandler((killer, npc) => kills.Add((killer, npc)));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(40);

        Assert.Equal([("ana", rat)], kills);
        Assert.True(rat.IsDead);
        Assert.DoesNotContain(Id(rat), ana.Npcs.Keys);
        Assert.Contains("You defeated the rat.", ana.Notices);
    }

    [Fact]
    public async Task ExperienceIsGrantedOnce()
    {
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(40);

        Assert.Equal(3 * DefaultCombat.Rules.FightingXpPerHealth, Server.Skills.XpOf("ana", "fighting"));
        Assert.Equal(3 * DefaultCombat.Rules.HealthXpPerHealth, Server.Skills.XpOf("ana", "health"));
    }

    [Fact]
    public async Task TwoBlowsOnOneTickKillItOnce()
    {
        var ana = await _test.JoinAsync("Ana");
        var bob = await _test.JoinAsync("Bob");
        var rat = Place(1, At(33, 32));
        var kills = 0;
        Server.Combat.AddKillHandler((_, _) => kills++);
        Server.Skills.Grant("bob", "fighting", 100000);
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await bob.AttackAsync(Id(rat));
        await _test.TickAsync(40);

        Assert.Equal(1, kills);
        var xp = Server.Skills.XpOf("ana", "fighting") + Server.Skills.XpOf("bob", "fighting");
        Assert.True(xp >= 3 * DefaultCombat.Rules.FightingXpPerHealth);
    }

    [Fact]
    public async Task ItStandsAgainAtHomeAfterItsRespawnTime()
    {
        await StartAsync(Foes(Rat(respawnTicks: 60)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);
        Assert.True(rat.IsDead);

        await _test.TickAsync(60);

        Assert.False(rat.IsDead);
        Assert.Equal(At(33, 32), rat.Tile);
        Assert.Equal(3, Server.Combat.HealthOfNpc(rat)?.Now);
    }

    [Fact]
    public async Task ACreatureFarFromEveryPlayerStaysDown()
    {
        await StartAsync(Foes(Rat(respawnTicks: 60)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);
        await ana.SendAsync(new Walk(At(120, 120)));
        await _test.TickAsync(160);

        Assert.True(rat.IsDead);
    }

    [Fact]
    public async Task ANewHealthLevelRaisesTheMostHitPoints()
    {
        // The creature lands its one hit and the new level heals one point.
        await StartAsync(Foes(Rat(speed: 100)));
        var ana = await _test.JoinAsync("Ana");
        var before = ana.HealthOf(ana.Avatar)!.Value;
        var rat = Place(1, At(33, 32));
        Server.Skills.Grant("ana", "health", DefaultSkills.All.Curve.XpForLevel(2) - 1);
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(20);

        var after = Server.Combat.HealthOfPlayer("ana");
        Assert.Equal((before.Now, before.Max + 1), after);
        Assert.Equal(after, ana.HealthOf(ana.Avatar));
    }

    // Being hit.

    [Fact]
    public async Task ACreatureSwingsBackAtItsOwnSpeed()
    {
        await StartAsync(Foes(Rat(health: 50, speed: 3)), new FightDice(0, damage: 0));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(13);

        Assert.InRange(ana.Hits.Count(hit => hit.Target == ana.Avatar), 3, 5);
    }

    [Fact]
    public async Task TheDamageShowsInTheHealthTheClientReads()
    {
        await StartAsync(Foes(Rat(health: 50)), new FightDice(0, damage: 1));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(6);

        var mine = ana.HealthOf(ana.Avatar)!.Value;
        Assert.True(mine.Now < mine.Max);
        Assert.Equal(Server.Combat.HealthOfPlayer("ana")?.Now, mine.Now);
        Assert.True(ana.HealthOf(Id(rat))?.Now < 50);
    }

    [Fact]
    public async Task AHitGoesToTheFighterAndNotToABystander()
    {
        await StartAsync(Foes(Rat(health: 50)));
        var ana = await _test.JoinAsync("Ana");
        var bob = await _test.JoinAsync("Bob");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(8);

        Assert.NotEmpty(ana.Hits);
        Assert.Empty(bob.Hits);
    }

    [Fact]
    public async Task AnAggressiveCreatureAdoptsAPlayerWhoStandsByIt()
    {
        await StartAsync(Foes(Rat(health: 50), new Aggressive(sight: 5, leash: 12)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(36, 32));
        await ana.SendAsync(new Walk(At(34, 32)));
        await _test.TickAsync(20);

        Assert.Contains(ana.Hits, hit => hit.Target == ana.Avatar);
        Assert.True(rat.IsEngaged);
    }

    [Fact]
    public async Task ArmourTurnsHitsIntoMisses()
    {
        // 0.3 beats an even chance but not one against 600 of defence.
        await StartAsync(Foes(Rat(health: 50)), new FightDice(0.3));
        var ana = await _test.JoinAsync("Ana");
        Wear("ana", Plate);
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);

        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(14);

        var taken = ana.Hits.Where(hit => hit.Target == ana.Avatar).ToList();
        Assert.NotEmpty(taken);
        Assert.All(taken, hit => Assert.Equal(0, hit.Damage));
    }

    [Fact]
    public async Task ACreatureThatIsHitStandsAndFights()
    {
        // Rolls of zero would send an idle wanderer off every tick.
        var dice = new FightDice(0.99);
        await StartAsync(Foes(Rat(health: 50), new Wander()), dice);
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32), radius: 5);
        await _test.TickAsync(2);

        dice.Hit = 0;
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(8);

        Assert.Equal(At(33, 32), rat.Tile);
        Assert.True(rat.IsEngaged);
    }

    [Fact]
    public async Task ACreatureLetsGoOfAPlayerWhoLeaves()
    {
        await StartAsync(Foes(Rat(health: 50)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(3);
        Assert.True(rat.IsEngaged);

        Server.Host.RemoveClient("ana");
        await _test.TickAsync(2);

        Assert.False(rat.IsEngaged);
    }

    [Fact]
    public async Task WalkingAwayEndsTheFight()
    {
        await StartAsync(Foes(Rat(health: 50)));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(6);

        await ana.SendAsync(new Walk(At(45, 45)));
        await _test.TickAsync(30);
        var seen = ana.Hits.Count;
        await _test.TickAsync(20);

        Assert.Equal(seen, ana.Hits.Count);
        Assert.False(rat.IsEngaged);
    }

    // Dying.

    /// <summary>Starts a fight against a rat that cannot lose, carrying one log, and runs until the player has died.</summary>
    private async Task<TestClient> LoseOnceAsync(int ratX = 38)
    {
        await StartAsync(Foes(Rat(health: 500, speed: 1)), new FightDice(0, damage: 99));
        var ana = await _test.JoinAsync("Ana");
        Server.Items.ContainerOf("ana", ContainerId.Inventory)!.Add(ItemId.Logs, 1);
        var rat = Place(1, At(ratX, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(25);
        return ana;
    }

    [Fact]
    public async Task ThePlayerIsBackAtTheSpawnTileWithFullHealth()
    {
        var ana = await LoseOnceAsync();

        var health = Server.Combat.HealthOfPlayer("ana")!.Value;
        Assert.Equal(health.Max, health.Now);
        Assert.Equal(health.Max, ana.HealthOf(ana.Avatar)?.Now);
        Assert.Equal(OpenWorld.SpawnTile, ana.TileOf(ana.Avatar));
        Assert.Contains("Oh dear, you are dead.", ana.Notices);
    }

    [Fact]
    public async Task ThePlayerKeepsWhatTheyCarried()
    {
        await LoseOnceAsync();

        Assert.Equal(1, Server.Items.ContainerOf("ana", ContainerId.Inventory)!.CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ACreatureByTheSpawnTileLetsTheDeadGo()
    {
        var ana = await LoseOnceAsync(ratX: 33);
        var seen = ana.Hits.Count;

        await _test.TickAsync(20);

        Assert.Equal(seen, ana.Hits.Count);
    }

    [Fact]
    public async Task TheCreatureStopsFightingAPlayerWhoDied()
    {
        var ana = await LoseOnceAsync();
        var seen = ana.Hits.Count;

        await _test.TickAsync(20);

        Assert.Equal(seen, ana.Hits.Count);
    }

    // Healing.

    [Fact]
    public async Task AHurtPlayerRegainsAPointOnlyAfterTheFight()
    {
        await StartAsync(Foes(Rat(health: 50)), new FightDice(0, damage: 1));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(6);
        await ana.SendAsync(new Walk(At(50, 50)));
        await _test.TickAsync(30);
        var hurt = Server.Combat.HealthOfPlayer("ana")!.Value.Now;

        await _test.TickAsync(DefaultCombat.Rules.RegenTicks * 2);

        var health = Server.Combat.HealthOfPlayer("ana")!.Value;
        Assert.True(hurt < health.Max);
        Assert.True(health.Now > hurt);
    }

    [Fact]
    public async Task NoPointComesBackDuringAFight()
    {
        await StartAsync(Foes(Rat(health: 5000, speed: 20)), new FightDice(0, damage: 0));
        var ana = await _test.JoinAsync("Ana");
        var rat = Place(1, At(33, 32));
        await _test.TickAsync(2);
        await ana.AttackAsync(Id(rat));
        await _test.TickAsync(DefaultCombat.Rules.RegenTicks + 5);

        var taken = ana.Hits.Where(hit => hit.Target == ana.Avatar).Sum(hit => hit.Damage);
        var health = Server.Combat.HealthOfPlayer("ana")!.Value;
        Assert.True(taken > 0);
        Assert.Equal(health.Max - taken, health.Now);
    }

    [Fact]
    public void RulesThatNameAMissingSkillFailAtStartup() =>
        Assert.Throws<ArgumentException>(() => Start(rules: new CombatRules(fightingSkill: "swords", healthSkill: "health")));

    /// <summary>
    /// Dice for a fight. A swing draws a hit roll and, on a hit, a damage roll. <see cref="Hit"/>
    /// answers the first. The damage answers the second, held under the most the roll allows.
    /// </summary>
    private sealed class FightDice(double hit, int damage = 0) : Random
    {
        public double Hit { get; set; } = hit;

        public override double NextDouble() => Hit;

        public override int Next(int maxValue) => Math.Min(damage, maxValue - 1);
    }
}
