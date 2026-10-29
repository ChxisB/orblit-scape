using System.Buffers.Binary;
using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>What combat asks of the rest of the server.</summary>
public sealed record CombatOptions
{
    /// <summary>Decides when an attack starts, since it is the attack option of a creature.</summary>
    public required Interactions Interactions { get; init; }

    public required CombatRules Rules { get; init; }

    public required Skills Skills { get; init; }

    public required ItemSet Items { get; init; }

    public required Items Bags { get; init; }

    public required Movement Movement { get; init; }

    public required Npcs Npcs { get; init; }

    public required Actors Actors { get; init; }

    public required Random Random { get; init; }

    /// <summary>Where a player who dies stands again.</summary>
    public required TileCoord Spawn { get; init; }

    /// <summary>The percent a prayer adds to a stat of a player, zero for none.</summary>
    public Func<string, CombatStat, int> Boost { get; init; } = (_, _) => 0;
}

/// <summary>
/// Fights between players and creatures. A player's click on a creature walks them up to it, or
/// within range of it for a bow, and starts an attack that repeats at their weapon's speed and
/// follows the creature if it moves. A shot or a spell flies for a few ticks and lands if its
/// target still stands. A creature that is hit, or an aggressive one that reaches its prey, swings
/// back at its own speed. The loser leaves: a creature goes until its respawn time, and a player
/// stands at the spawn tile again with all they carried.
/// </summary>
public sealed class Combat
{
    // A tick far enough back that nobody was fighting then.
    private const long LongAgo = -1_000_000;

    private static readonly int AmmoSlot = ItemDef.EquipSlots.ToList().IndexOf("ammo");

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly CombatOptions _options;
    private readonly ComponentType _health;
    private readonly Dictionary<string, Fighter> _players = [];
    private readonly Dictionary<PlacedNpc, Fighter> _creatures = [];
    private readonly OrderedDictionary<PlacedNpc, string> _opponent = [];
    private readonly List<Action<string, PlacedNpc>> _killHandlers = [];
    private readonly List<Flight> _flights = [];

    // Checked every tick, so the attack follows a creature that moves.
    private readonly ActionKind _attack = new("attack", 1);

    // Queued to cancel what a player had waiting when they die or teleport.
    private readonly ActionKind _interrupt = new("interrupt", cancels: new CancelsAll());

    private Combat(Registry registry, Services services, CombatOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
        _health = services.World.RegisterComponent(Health.Name, ComponentKind.Int32, Health.Ints);
    }

    /// <summary>Adds combat to <paramref name="registry"/> and returns the feature, so loot can listen for a kill.</summary>
    /// <exception cref="ArgumentException">The rules name a skill that is not there.</exception>
    public static Combat Register(Registry registry, Services services, CombatOptions options)
    {
        options.Skills.LevelOf("", options.Rules.FightingSkill);
        options.Skills.LevelOf("", options.Rules.HealthSkill);
        var feature = new Combat(registry, services, options);
        registry.Replicate(feature._health);
        options.Interactions.AddNpcAction(NpcOption.Attack, feature.Begin, feature.ReachOf);
        options.Npcs.AddStandHandler(feature.Stood);
        registry.AddKind(feature._attack);
        registry.AddKind(feature._interrupt);
        registry.AddJoinHandler(feature.Join);
        registry.AddGoneHandler(feature.Gone);
        registry.AddSystem(Phase.Combat, feature.Turn);
        return feature;
    }

    private long Tick => _registry.Actions.Tick;

    /// <summary>Calls <paramref name="handler"/> with the killer and the creature each time a player defeats a creature.</summary>
    public void AddKillHandler(Action<string, PlacedNpc> handler) => _killHandlers.Add(handler);

    /// <summary>The hit points of <paramref name="player"/>, or null when they are not in the world.</summary>
    public (int Now, int Max)? HealthOfPlayer(string player) =>
        _players.TryGetValue(player, out var fighter) ? (fighter.Health, fighter.Max) : null;

    /// <summary>The hit points of <paramref name="npc"/>, or null when it is dead or cannot fight.</summary>
    public (int Now, int Max)? HealthOfNpc(PlacedNpc npc) =>
        _creatures.TryGetValue(npc, out var fighter) ? (fighter.Health, fighter.Max) : null;

    /// <summary>
    /// Whether <paramref name="player"/> swung or was swung at within the last
    /// <see cref="CombatRules.CombatTicks"/> ticks.
    /// </summary>
    public bool IsFighting(string player) => _players.TryGetValue(player, out var fighter) && Recently(fighter);

    /// <summary>Whether <paramref name="player"/> may swing, shoot or cast now.</summary>
    public bool IsReady(string player) => _players.TryGetValue(player, out var fighter) && fighter.ReadyAt <= Tick;

    /// <summary>
    /// Casts <paramref name="spell"/> with a magic skill of <paramref name="level"/>. A damage spell
    /// flies at <paramref name="at"/>, a stun holds it at once and a teleport takes the caster away,
    /// so only a teleport may have no creature. The caller has checked the range, the level and the
    /// cost.
    /// </summary>
    public void Cast(string player, SpellDef spell, int level, PlacedNpc? at)
    {
        if (!_players.TryGetValue(player, out var fighter))
        {
            return;
        }

        switch (spell.Effect)
        {
            case SpellDamage damage:
                Hurt(player, at!, damage.Most, level); // A spell that hits has a creature.
                break;
            case SpellStun stun:
                Stun(player, at!, stun.Ticks); // A spell that hits has a creature.
                break;
            case SpellTeleport teleport:
                Carry(player, teleport.To);
                break;
        }

        fighter.ReadyAt = Tick + spell.Speed;
        if (spell.Effect is not SpellTeleport)
        {
            fighter.LastFight = Tick;
            _options.Actors.Play(_options.Movement.ActorOf(player)!.Value, ActorState.Attack); // They are casting.
        }
    }

    /// <summary>Raises the most hit points of <paramref name="player"/> to their level and heals them by the rise.</summary>
    public void Refit(string player)
    {
        if (!_players.TryGetValue(player, out var fighter))
        {
            return;
        }

        var max = MaxHealthOf(player);
        fighter.Health += max - fighter.Max;
        fighter.Max = max;
        Write(_options.Movement.ActorOf(player)!.Value, fighter); // A fighter is a player in the world.
    }

    private static int Distance(TileCoord a, TileCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Where a player stands to attack with <paramref name="gear"/>: beside the creature, or within the range of the weapon they shoot.</summary>
    private static Reach ReachWith(Gear gear, PlacedNpc npc)
    {
        var target = Footprint.OfTile(npc.Tile);
        return gear.Range is { } range ? new Within(target, range) : new Beside(target);
    }

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    private bool Recently(Fighter fighter) => Tick - fighter.LastFight < _options.Rules.CombatTicks;

    private int MaxHealthOf(string player) =>
        _options.Rules.MaxHealth(_options.Skills.LevelOf(player, _options.Rules.HealthSkill));

    private void Join(string key)
    {
        var entity = _options.Movement.ActorOf(key)!.Value; // Movement joined them first.
        _services.World.Add(entity, _health);
        var fighter = new Fighter(MaxHealthOf(key));
        _players[key] = fighter;
        Write(entity, fighter);
    }

    private void Gone(string key) => _players.Remove(key);

    private void Stood(PlacedNpc npc)
    {
        if (npc.Def.Fight is not { } fight)
        {
            return;
        }

        _services.World.Add(npc.Entity, _health);
        var fighter = new Fighter(fight.Health);
        _creatures[npc] = fighter;
        Write(npc.Entity, fighter);
    }

    private void Write(ulong entity, Fighter fighter)
    {
        if (_services.World.TryGetBytes(entity, _health, out var bytes))
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, fighter.Health);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], fighter.Max);
        }
    }

    private void Hurt(string player, PlacedNpc npc, int most, int level)
    {
        var fight = npc.Def.Fight!; // Magic refused a creature that cannot fight.
        var rules = _options.Rules;
        var damage = rules.Damage(
            _options.Random,
            most,
            attack: new Rating(level, 0),
            defence: new Rating(fight.Defence, 0),
            edge: rules.Edge(CombatStyle.Magic, fight.Style));
        Shoot(player, npc, damage, CombatStyle.Magic);
    }

    private void Stun(string player, PlacedNpc npc, int ticks)
    {
        var target = _creatures[npc]; // Magic refused a creature that cannot fight.
        target.ReadyAt = Math.Max(target.ReadyAt, Tick + ticks);
        Provoke(player, npc, target);
    }

    private Reach ReachOf(string player, PlacedNpc npc) => ReachWith(GearOf(player), npc);

    private void Begin(string player, PlacedNpc npc)
    {
        if (npc.Def.Fight is null)
        {
            Tell(player, "You cannot attack that.");
            return;
        }

        Round(player, npc);
    }

    private void Queue(string player, PlacedNpc npc)
    {
        if (_options.Movement.ActorOf(player) is { } actor)
        {
            _registry.Actions.Submit(actor, _attack, () => Round(player, npc));
        }
    }

    /// <summary>
    /// One tick of the attack of <paramref name="player"/>: close in when the creature has moved
    /// off, swing or shoot when the weapon is ready, and come back next tick. A bow with no ammo
    /// ends the attack.
    /// </summary>
    private void Round(string player, PlacedNpc npc)
    {
        if (!_players.TryGetValue(player, out var fighter) || _options.Movement.TileOf(player) is not { } tile || npc.IsDead)
        {
            return;
        }

        var gear = GearOf(player);
        var reach = ReachWith(gear, npc);
        if (!reach.IsSatisfied(_services.Map, tile))
        {
            Follow(player, tile, reach, npc);
            return;
        }

        _options.Movement.Follow(player, []);
        if (fighter.ReadyAt <= Tick)
        {
            if (!gear.Loaded)
            {
                Tell(player, "You have no ammo.");
                return;
            }

            Strike(player, fighter, npc, gear);
        }

        if (!npc.IsDead)
        {
            Queue(player, npc);
        }
    }

    private void Follow(string player, TileCoord tile, Reach reach, PlacedNpc npc)
    {
        var path = PathSearch.FindToReach(_services.Map, tile, reach);
        if (!path.Reached)
        {
            Tell(player, "You cannot reach that.");
            return;
        }

        _options.Movement.Follow(player, path.Steps);
        Queue(player, npc);
    }

    private void Strike(string player, Fighter fighter, PlacedNpc npc, Gear gear)
    {
        var fight = npc.Def.Fight!; // Begin refused a creature without one.
        var rules = _options.Rules;
        var damage = rules.Swing(
            _options.Random,
            attack: new Rating(Boosted(player, CombatStat.Attack), gear.Attack),
            strength: new Rating(Boosted(player, CombatStat.Strength), gear.Strength),
            defence: new Rating(fight.Defence, 0),
            edge: rules.Edge(gear.Style, fight.Style));
        fighter.ReadyAt = Tick + (gear.Weapon?.Speed ?? rules.UnarmedSpeed);
        fighter.LastFight = Tick;
        _options.Actors.Play(_options.Movement.ActorOf(player)!.Value, ActorState.Attack); // Round read their tile.
        if (gear.Range is null)
        {
            Land(player, npc, damage);
            return;
        }

        _options.Bags.ContainerOf(player, ContainerId.Equipment)!.Take(AmmoSlot, 1); // Gear found ammo there.
        Shoot(player, npc, damage, gear.Style);
    }

    /// <summary>The fighting level of <paramref name="player"/> with the prayer bonus on <paramref name="stat"/>.</summary>
    private int Boosted(string player, CombatStat stat)
    {
        var level = _options.Skills.LevelOf(player, _options.Rules.FightingSkill);
        return level + (((level * _options.Boost(player, stat)) + 50) / 100);
    }

    private void Shoot(string player, PlacedNpc npc, int damage, CombatStyle style)
    {
        var from = _options.Movement.Standing(player)!.Value; // The caller read their tile.
        var ticks = _options.Rules.FlightTicks(Distance(from.Tile, npc.Tile));
        _services.Hosting.Tell(player, new Projectile((long)from.NetworkId, (long)npc.NetworkId, ticks, style));
        _flights.Add(new Flight
        {
            Player = player,
            Npc = npc,
            Target = _creatures[npc], // A creature that fights has a fighter.
            Damage = damage,
            Lands = Tick + ticks,
        });
    }

    /// <summary>Lands a hit of <paramref name="damage"/> from <paramref name="player"/> on <paramref name="npc"/>, which still stands.</summary>
    private void Land(string player, PlacedNpc npc, int damage)
    {
        var target = _creatures[npc];
        Provoke(player, npc, target);
        _services.Hosting.Tell(player, new Hit((long)npc.NetworkId, damage));
        target.Health = Math.Max(0, target.Health - damage);
        Write(npc.Entity, target);
        Flinch(npc.Entity, damage);
        if (target.Health == 0)
        {
            Defeat(player, npc);
        }
    }

    // A hit that took something off makes its target recoil.
    private void Flinch(ulong entity, int damage)
    {
        if (damage > 0)
        {
            _options.Actors.Play(entity, ActorState.Hurt);
        }
    }

    /// <summary>Makes <paramref name="player"/> the opponent of <paramref name="npc"/>, which then swings back if it is beside them.</summary>
    private void Provoke(string player, PlacedNpc npc, Fighter target)
    {
        target.LastFight = Tick;
        _opponent.TryAdd(npc, player);
        npc.IsEngaged = true;
    }

    private void LandFlights()
    {
        var tick = Tick;
        var due = _flights.Where(flight => flight.Lands <= tick).ToList();
        _flights.RemoveAll(flight => flight.Lands <= tick);
        foreach (var flight in due)
        {
            // A creature that died and stood again has a new fighter, which the shot misses.
            if (_creatures.GetValueOrDefault(flight.Npc) == flight.Target && _players.ContainsKey(flight.Player))
            {
                Land(flight.Player, flight.Npc, flight.Damage);
            }
        }
    }

    private void Defeat(string player, PlacedNpc npc)
    {
        var fight = npc.Def.Fight!; // Only a creature that fights loses health.
        var rules = _options.Rules;
        _options.Actors.Play(npc.Entity, ActorState.Death);
        _options.Npcs.Kill(npc);
        _opponent.Remove(npc);
        _creatures.Remove(npc);
        Tell(player, $"You defeated the {npc.Def.Name.ToLowerInvariant()}.");
        Grant(player, rules.FightingSkill, fight.Health * rules.FightingXpPerHealth);
        Grant(player, rules.HealthSkill, fight.Health * rules.HealthXpPerHealth);
        foreach (var handler in _killHandlers)
        {
            handler(player, npc);
        }
    }

    private void Grant(string player, string skill, long xp)
    {
        var reached = _options.Skills.Award(player, skill, xp);
        if (reached.Count > 0 && skill == _options.Rules.HealthSkill)
        {
            Refit(player);
        }
    }

    /// <summary>The gear of <paramref name="player"/>. Ammo counts only when the weapon shoots it.</summary>
    private Gear GearOf(string player)
    {
        var slots = WornBonuses(player);
        var used = slots.Where((_, slot) => slot != AmmoSlot).OfType<CombatBonus>().ToList();
        var weapon = used.Find(bonus => bonus.Speed is not null);
        var ammo = slots[AmmoSlot];
        var shoots = weapon?.Style == CombatStyle.Ranged;
        var loaded = !shoots || ammo?.Ammo == weapon?.Ammo;
        if (shoots && loaded)
        {
            used.Add(ammo!); // Its kind matched the weapon's, so it is there.
        }

        return new Gear { Weapon = weapon, Used = used, Loaded = loaded };
    }

    /// <summary>The bonus of what <paramref name="player"/> wears in each slot, or null where it gives none.</summary>
    private List<CombatBonus?> WornBonuses(string player)
    {
        var worn = _options.Bags.ContainerOf(player, ContainerId.Equipment);
        return [.. Enumerable.Range(0, ItemDef.EquipSlots.Count).Select(slot => _options.Items.Find(worn?.At(slot)?.Item ?? 0)?.Combat)];
    }

    /// <summary>The combat phase: shots land, creatures swing, then everyone heals a point on a regeneration tick.</summary>
    private void Turn()
    {
        LandFlights();
        AdoptPrey();
        foreach (var (npc, player) in _opponent.ToList())
        {
            SwingBack(npc, player);
        }

        if (Tick % _options.Rules.RegenTicks != 0)
        {
            return;
        }

        foreach (var (player, fighter) in _players)
        {
            Heal(_options.Movement.ActorOf(player)!.Value, fighter); // A fighter is a player in the world.
        }

        foreach (var (npc, fighter) in _creatures)
        {
            Heal(npc.Entity, fighter);
        }
    }

    // An aggressive creature that has caught up with a player fights them.
    private void AdoptPrey()
    {
        var hunters = _options.Npcs.Standing
            .Where(npc => npc.Hunting && npc.Def.Fight is not null && !_opponent.ContainsKey(npc))
            .ToList();
        foreach (var npc in hunters)
        {
            if (_options.Movement.Players.FirstOrDefault(player => IsBeside(npc, player)) is { } player)
            {
                _opponent[npc] = player;
                npc.IsEngaged = true;
            }
        }
    }

    private bool IsBeside(PlacedNpc npc, string player) =>
        _options.Movement.TileOf(player) is { } tile
        && tile.Plane == npc.Tile.Plane
        && new Beside(Footprint.OfTile(tile)).IsSatisfied(_services.Map, npc.Tile);

    private void SwingBack(PlacedNpc npc, string player)
    {
        // A player who died earlier this phase let go of every creature fighting them.
        if (_opponent.GetValueOrDefault(npc) != player)
        {
            return;
        }

        if (!_players.TryGetValue(player, out var target) || !_creatures.TryGetValue(npc, out var fighter) || !IsBeside(npc, player))
        {
            Drop(npc);
            return;
        }

        if (fighter.ReadyAt <= Tick)
        {
            Swing(npc, fighter, player, target);
        }
    }

    private void Swing(PlacedNpc npc, Fighter fighter, string player, Fighter target)
    {
        var fight = npc.Def.Fight!; // Only a creature that fights has a fighter.
        var gear = GearOf(player);
        var rules = _options.Rules;
        var damage = rules.Swing(
            _options.Random,
            attack: new Rating(fight.Attack, 0),
            strength: new Rating(fight.Strength, 0),
            defence: new Rating(Boosted(player, CombatStat.Defence), gear.Defence),
            edge: rules.Edge(fight.Style, gear.Style));
        fighter.ReadyAt = Tick + fight.Speed;
        fighter.LastFight = Tick;
        target.LastFight = Tick;
        _options.Actors.Play(npc.Entity, ActorState.Attack);
        var actor = _options.Movement.ActorOf(player)!.Value; // IsBeside found their tile.
        _services.Hosting.Tell(player, new Hit((long)_options.Movement.Standing(player)!.Value.NetworkId, damage));
        target.Health = Math.Max(0, target.Health - damage);
        Write(actor, target);
        Flinch(actor, damage);
        if (target.Health == 0)
        {
            Die(player, target);
        }
    }

    private void Die(string player, Fighter fighter)
    {
        fighter.Health = fighter.Max;
        fighter.LastFight = Tick;
        Carry(player, _options.Spawn);
        Write(_options.Movement.ActorOf(player)!.Value, fighter); // Carry found them.
        Tell(player, "Oh dear, you are dead.");
    }

    /// <summary>
    /// Takes <paramref name="player"/> to <paramref name="to"/> and ends what they were doing: their
    /// attack, the creatures fighting them and the shots they had in the air.
    /// </summary>
    private void Carry(string player, TileCoord to)
    {
        foreach (var npc in _opponent.Where(pair => pair.Value == player).Select(pair => pair.Key).ToList())
        {
            Drop(npc);
        }

        _flights.RemoveAll(flight => flight.Player == player);
        _registry.Actions.Submit(_options.Movement.ActorOf(player)!.Value, _interrupt, () => { }); // The caller found them.
        _options.Movement.Teleport(player, to);
    }

    private void Drop(PlacedNpc npc)
    {
        _opponent.Remove(npc);
        npc.IsEngaged = false;
    }

    private void Heal(ulong entity, Fighter fighter)
    {
        if (fighter.Health >= fighter.Max || Recently(fighter))
        {
            return;
        }

        fighter.Health++;
        Write(entity, fighter);
    }

    private sealed class Fighter(int max)
    {
        public int Health { get; set; } = max;

        public int Max { get; set; } = max;

        /// <summary>The first tick this fighter may swing.</summary>
        public long ReadyAt { get; set; }

        /// <summary>The last tick this fighter swung or was swung at.</summary>
        public long LastFight { get; set; } = LongAgo;
    }

    /// <summary>What a fight reads of the gear a player wears.</summary>
    private sealed class Gear
    {
        /// <summary>The worn item with a speed, or null for bare hands.</summary>
        public required CombatBonus? Weapon { get; init; }

        /// <summary>The worn items that count, with the ammo when the weapon shoots it.</summary>
        public required IReadOnlyList<CombatBonus> Used { get; init; }

        /// <summary>False for a bow with no matching ammo in the ammo slot.</summary>
        public required bool Loaded { get; init; }

        public int Attack => Used.Sum(bonus => bonus.Attack);

        public int Strength => Used.Sum(bonus => bonus.Strength);

        public int Defence => Used.Sum(bonus => bonus.Defence);

        public CombatStyle Style => Weapon?.Style ?? CombatStyle.Melee;

        /// <summary>Tiles the weapon shoots, or null for one that is not shot.</summary>
        public int? Range => Weapon?.Range;
    }

    /// <summary>A shot or a spell on its way. It lands unless the creature it was aimed at has died or been replaced.</summary>
    private sealed class Flight
    {
        public required string Player { get; init; }

        public required PlacedNpc Npc { get; init; }

        public required Fighter Target { get; init; }

        public required int Damage { get; init; }

        public required long Lands { get; init; }
    }
}
