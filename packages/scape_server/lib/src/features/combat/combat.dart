import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/action.dart';
import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../npcs/npcs.dart';
import '../skills/skills.dart';

/// What a fight reads of the gear a player wears. [range] is null for a
/// weapon that is not shot, and [loaded] is false for a bow with no matching
/// ammo in the ammo slot.
typedef _Gear = ({
  int attack,
  int strength,
  int defence,
  int speed,
  CombatStyle style,
  int? range,
  bool loaded,
});

/// The percent a prayer adds to [stat] of [player], zero for none.
typedef Boost = int Function(String player, CombatStat stat);

/// A shot or a spell on its way: it lands on [lands] unless the creature it
/// was aimed at has died or been replaced by then.
typedef _Flight = ({
  String player,
  PlacedNpc npc,
  _Fighter target,
  int damage,
  int lands,
});

int _noBoost(String player, CombatStat stat) => 0;

final int _ammoSlot = equipmentSlots.indexOf('ammo');

/// What a feature does when [killer] defeats [npc]. It runs once per death.
typedef KillHandler = void Function(String killer, PlacedNpc npc);

/// A tick far enough back that nobody was fighting then.
const int _longAgo = -1000000;

final class _Fighter {
  _Fighter(this.max) : health = max;

  int health;
  int max;

  /// The first tick this fighter may swing.
  int readyAt = 0;

  /// The last tick this fighter swung or was swung at.
  int lastFight = _longAgo;
}

/// Fights between players and creatures. A player's click on a creature walks
/// them up to it, or within range of it for a bow, and starts an attack that
/// repeats at their weapon's speed and follows the creature if it moves. A shot
/// or a spell flies for a few ticks and lands if its target still stands. A
/// creature that is hit, or an aggressive one that reaches its prey, swings
/// back at its own speed. The loser leaves: a creature goes until its respawn
/// time, and a player stands at the spawn tile again with all they carried.
final class Combat {
  Combat._(
    this._registry,
    this._services, {
    required CombatRules rules,
    required Skills skills,
    required ItemSet items,
    required Items bags,
    required Movement movement,
    required Npcs npcs,
    required Random random,
    required TileCoord spawn,
    required ComponentType health,
    required Boost boost,
  }) : _rules = rules,
       _skills = skills,
       _items = items,
       _bags = bags,
       _movement = movement,
       _npcs = npcs,
       _random = random,
       _spawn = spawn,
       _health = health,
       _boost = boost;

  /// Adds combat to [registry] and returns the feature, so loot can listen
  /// for a kill.
  factory Combat.register(
    Registry registry,
    Services services, {
    required Interactions interactions,
    required CombatRules rules,
    required Skills skills,
    required ItemSet items,
    required Items bags,
    required Movement movement,
    required Npcs npcs,
    required Random random,
    required TileCoord spawn,
    Boost? boost,
  }) {
    // These throw at startup when the rules name a skill that is not there.
    skills
      ..levelOf('', rules.fightingSkill)
      ..levelOf('', rules.healthSkill);
    final health = services.world.registerComponent(
      healthName,
      kind: ComponentKind.int32,
      arity: healthInts,
    );
    registry.replicate(health);
    final feature = Combat._(
      registry,
      services,
      rules: rules,
      skills: skills,
      items: items,
      bags: bags,
      movement: movement,
      npcs: npcs,
      random: random,
      spawn: spawn,
      health: health,
      boost: boost ?? _noBoost,
    );
    interactions.addNpcAction(
      NpcOption.attack,
      feature._begin,
      reach: feature._reachOf,
    );
    npcs.addStandHandler(feature._stood);
    registry
      ..addKind(feature._attack)
      ..addKind(feature._revive)
      ..addJoinHandler(feature._join)
      ..addGoneHandler(feature._gone)
      ..addSystem(Phase.combat, feature._turn);
    return feature;
  }

  final Registry _registry;
  final Services _services;
  final CombatRules _rules;
  final Skills _skills;
  final ItemSet _items;
  final Items _bags;
  final Movement _movement;
  final Npcs _npcs;
  final Random _random;
  final TileCoord _spawn;
  final ComponentType _health;
  final Boost _boost;
  final Map<String, _Fighter> _players = {};
  final Map<PlacedNpc, _Fighter> _creatures = {};
  final Map<PlacedNpc, String> _opponent = {};
  final List<KillHandler> _killHandlers = [];
  final List<_Flight> _flights = [];

  /// Checked every tick, so the attack follows a creature that moves.
  final ActionKind _attack = ActionKind('attack', delay: 1);

  /// Queued to cancel what a player had waiting when they die.
  final ActionKind _revive = ActionKind('revive', cancels: const CancelsAll());

  int get _tick => _registry.actions.tick;

  /// Calls [handler] each time a player defeats a creature.
  void addKillHandler(KillHandler handler) => _killHandlers.add(handler);

  /// The hit points of [player], or null when they are not in the world.
  ({int now, int max})? healthOfPlayer(String player) {
    final fighter = _players[player];
    return fighter == null ? null : (now: fighter.health, max: fighter.max);
  }

  /// Whether [player] swung or was swung at within the last
  /// [CombatRules.combatTicks] ticks.
  bool isFighting(String player) {
    final fighter = _players[player];
    return fighter != null && _recently(fighter);
  }

  bool _recently(_Fighter fighter) =>
      _tick - fighter.lastFight < _rules.combatTicks;

  /// The hit points of [npc], or null when it is dead or cannot fight.
  ({int now, int max})? healthOfNpc(PlacedNpc npc) {
    final fighter = _creatures[npc];
    return fighter == null ? null : (now: fighter.health, max: fighter.max);
  }

  void _notice(String player, String text) =>
      _services.hosting.tell(player, Notice(text));

  void _join(String key) {
    final entity = _movement.actorOf(key)!; // movement joined them first
    _services.world.add(entity, _health);
    final fighter = _Fighter(_maxHealth(key));
    _players[key] = fighter;
    _write(entity, fighter);
  }

  void _gone(String key) => _players.remove(key);

  void _stood(PlacedNpc npc) {
    final fight = npc.def.fight;
    if (fight == null) return;
    _services.world.add(npc.entity, _health);
    final fighter = _Fighter(fight.health);
    _creatures[npc] = fighter;
    _write(npc.entity, fighter);
  }

  int _maxHealth(String player) =>
      _rules.maxHealth(_skills.levelOf(player, _rules.healthSkill));

  void _write(int entity, _Fighter fighter) {
    ByteData.sublistView(_services.world.bytesOf(entity, _health)!)
      ..setInt32(0, fighter.health, Endian.little)
      ..setInt32(4, fighter.max, Endian.little);
  }

  /// Whether [player] may swing, shoot or cast now.
  bool isReady(String player) {
    final fighter = _players[player];
    return fighter != null && fighter.readyAt <= _tick;
  }

  /// Casts [spell] at [npc] with a magic skill of [level]. A damage spell
  /// flies and a stun holds the creature at once. The caller has checked the
  /// range, the level and the cost.
  void cast(String player, PlacedNpc npc, SpellDef spell, int level) {
    final fight = npc.def.fight;
    final fighter = _players[player];
    if (fight == null || fighter == null || npc.isDead) return;
    switch (spell.effect) {
      case SpellDamage(:final most):
        final damage = _rules.damage(
          _random,
          most: most,
          attack: (level: level, bonus: 0),
          defence: (level: fight.defence, bonus: 0),
          edge: _rules.edge(CombatStyle.magic, fight.style),
        );
        _shoot(player, npc, damage, CombatStyle.magic);
      case SpellStun(:final ticks):
        final target = _creatures[npc]!; // a creature that fights has a fighter
        target.readyAt = max(target.readyAt, _tick + ticks);
        _provoke(player, npc, target);
      case SpellTeleport():
        return; // there is no creature to hit
    }
    fighter
      ..readyAt = _tick + spell.speed
      ..lastFight = _tick;
  }

  /// Where [player] stands to attack [npc]: beside it, or within the range of
  /// the weapon they shoot.
  Reach _reachOf(String player, PlacedNpc npc) {
    final target = Footprint.single(npc.tile);
    final range = _gearOf(player).range;
    return range == null ? Beside(target) : Within(target, range: range);
  }

  void _begin(String player, PlacedNpc npc) {
    if (npc.def.fight == null) {
      _notice(player, 'You cannot attack that.');
      return;
    }
    _round(player, npc);
  }

  void _queue(String player, PlacedNpc npc) {
    final actor = _movement.actorOf(player);
    if (actor == null) return;
    _registry.actions.submit(actor, _attack, () => _round(player, npc));
  }

  /// One tick of [player]'s attack: close in when the creature has moved off,
  /// swing or shoot when the weapon is ready, and come back next tick. A bow
  /// with no ammo ends the attack.
  void _round(String player, PlacedNpc npc) {
    final fighter = _players[player];
    final tile = _movement.tileOf(player);
    if (fighter == null || tile == null || npc.isDead) return;
    final reach = _reachOf(player, npc);
    if (!reachSatisfied(_services.map, reach, tile)) {
      _follow(player, tile, reach, npc);
      return;
    }
    _movement.follow(player, const []);
    if (fighter.readyAt <= _tick) {
      final gear = _gearOf(player);
      if (!gear.loaded) return _notice(player, 'You have no ammo.');
      _strike(player, fighter, npc, gear);
    }
    if (!npc.isDead) _queue(player, npc);
  }

  void _follow(String player, TileCoord tile, Reach reach, PlacedNpc npc) {
    final path = findPathToReach(_services.map, tile, reach);
    if (!path.reached) {
      _notice(player, 'You cannot reach that.');
      return;
    }
    _movement.follow(player, path.steps);
    _queue(player, npc);
  }

  void _strike(String player, _Fighter fighter, PlacedNpc npc, _Gear gear) {
    final fight = npc.def.fight!; // _begin refused a creature without one
    final damage = _rules.swing(
      _random,
      attack: (level: _boosted(player, CombatStat.attack), bonus: gear.attack),
      strength: (
        level: _boosted(player, CombatStat.strength),
        bonus: gear.strength,
      ),
      defence: (level: fight.defence, bonus: 0),
      edge: _rules.edge(gear.style, fight.style),
    );
    fighter
      ..readyAt = _tick + gear.speed
      ..lastFight = _tick;
    if (gear.range == null) {
      _land(player, npc, damage);
    } else {
      _bags.container(player, ContainerId.equipment)!.take(_ammoSlot, 1);
      _shoot(player, npc, damage, gear.style);
    }
  }

  /// The fighting level of [player] with the prayer bonus on [stat].
  int _boosted(String player, CombatStat stat) {
    final level = _skills.levelOf(player, _rules.fightingSkill);
    return level + level * _boost(player, stat) ~/ 100;
  }

  void _shoot(String player, PlacedNpc npc, int damage, CombatStyle style) {
    final from = _movement.standing(player)!; // _round read their tile
    final ticks = _rules.flightTicks(_distance(from.tile, npc.tile));
    _services.hosting.tell(
      player,
      Projectile(
        from: from.networkId,
        to: npc.networkId,
        ticks: ticks,
        style: style,
      ),
    );
    _flights.add((
      player: player,
      npc: npc,
      target: _creatures[npc]!, // a creature that fights has a fighter
      damage: damage,
      lands: _tick + ticks,
    ));
  }

  int _distance(TileCoord a, TileCoord b) =>
      max((a.x - b.x).abs(), (a.y - b.y).abs());

  /// Lands a hit of [damage] from [player] on [npc] that still stands.
  void _land(String player, PlacedNpc npc, int damage) {
    final target = _creatures[npc]!; // the callers checked it still stands
    _provoke(player, npc, target);
    _services.hosting.tell(player, Hit(target: npc.networkId, damage: damage));
    target.health = max(0, target.health - damage);
    _write(npc.entity, target);
    if (target.health == 0) _defeat(player, npc);
  }

  /// Makes [player] the opponent of [npc], which then swings back if it is
  /// beside them.
  void _provoke(String player, PlacedNpc npc, _Fighter target) {
    target.lastFight = _tick;
    _opponent.putIfAbsent(npc, () => player);
    npc.engage();
  }

  void _landFlights() {
    final due = [
      for (final flight in _flights)
        if (flight.lands <= _tick) flight,
    ];
    _flights.removeWhere((flight) => flight.lands <= _tick);
    for (final (:player, :npc, :target, :damage, lands: _) in due) {
      if (_creatures[npc] == target && _players.containsKey(player)) {
        _land(player, npc, damage);
      }
    }
  }

  void _defeat(String player, PlacedNpc npc) {
    final fight = npc.def.fight!;
    _npcs.kill(npc);
    _opponent.remove(npc);
    _creatures.remove(npc);
    _notice(player, 'You defeated the ${npc.def.name.toLowerCase()}.');
    _grant(
      player,
      _rules.fightingSkill,
      fight.health * _rules.fightingXpPerHealth,
    );
    _grant(player, _rules.healthSkill, fight.health * _rules.healthXpPerHealth);
    for (final handler in _killHandlers) {
      handler(player, npc);
    }
  }

  void _grant(String player, String skill, int xp) {
    final reached = _skills.grant(player, skill, xp);
    for (final level in reached) {
      _notice(player, 'You reached level $level in $skill.');
    }
    if (reached.isNotEmpty && skill == _rules.healthSkill) refit(player);
  }

  /// Raises the most hit points to the new level and heals by the rise.
  void refit(String player) {
    final fighter = _players[player];
    if (fighter == null) return;
    final max = _maxHealth(player);
    fighter
      ..health += max - fighter.max
      ..max = max;
    _write(_movement.actorOf(player)!, fighter);
  }

  /// What each equipment slot of [player] adds to a fight, null for a slot
  /// that is empty or adds nothing.
  List<CombatBonus?> _wornBonuses(String player) {
    final worn = _bags.container(player, ContainerId.equipment);
    if (worn == null) return List.filled(equipmentSlots.length, null);
    return [
      for (var slot = 0; slot < worn.size; slot++)
        _items.find(worn.at(slot)?.item ?? 0)?.combat,
    ];
  }

  /// The gear of [player]. Ammo counts only when the weapon shoots it.
  _Gear _gearOf(String player) {
    final slots = _wornBonuses(player);
    final ammo = slots[_ammoSlot];
    final worn = [
      for (final (slot, bonus) in slots.indexed)
        if (slot != _ammoSlot) ?bonus,
    ];
    final weapon = worn.where((bonus) => bonus.speed != null).firstOrNull;
    final style = weapon?.style ?? CombatStyle.melee;
    final shoots = style == CombatStyle.ranged;
    final loaded = !shoots || ammo?.ammo == weapon?.ammo;
    final used = [...worn, if (shoots && loaded) ?ammo];
    return (
      attack: used.fold(0, (sum, bonus) => sum + bonus.attack),
      strength: used.fold(0, (sum, bonus) => sum + bonus.strength),
      defence: used.fold(0, (sum, bonus) => sum + bonus.defence),
      speed: weapon?.speed ?? _rules.unarmedSpeed,
      style: style,
      range: weapon?.range,
      loaded: loaded,
    );
  }

  /// The combat phase: shots land, creatures swing, then everyone heals a
  /// point on a regeneration tick.
  void _turn() {
    _landFlights();
    _adoptPrey();
    for (final MapEntry(key: npc, value: player)
        in _opponent.entries.toList()) {
      _swingBack(npc, player);
    }
    if (_tick % _rules.regenTicks != 0) return;
    for (final MapEntry(key: player, value: fighter) in _players.entries) {
      _heal(_movement.actorOf(player)!, fighter);
    }
    for (final MapEntry(key: npc, value: fighter) in _creatures.entries) {
      _heal(npc.entity, fighter);
    }
  }

  /// An aggressive creature that has caught up with a player fights them.
  void _adoptPrey() {
    final hunters = _npcs.standing.where(
      (npc) =>
          npc.isHunting && npc.def.fight != null && !_opponent.containsKey(npc),
    );
    for (final npc in hunters.toList()) {
      for (final player in _movement.players) {
        if (!_beside(npc, player)) continue;
        _opponent[npc] = player;
        npc.engage();
        break;
      }
    }
  }

  bool _beside(PlacedNpc npc, String player) {
    final tile = _movement.tileOf(player);
    return tile != null &&
        tile.plane == npc.tile.plane &&
        reachSatisfied(_services.map, Beside(Footprint.single(tile)), npc.tile);
  }

  void _swingBack(PlacedNpc npc, String player) {
    final target = _players[player];
    final fighter = _creatures[npc];
    if (_opponent[npc] != player) return;
    if (target == null || fighter == null || !_beside(npc, player)) {
      _drop(npc);
      return;
    }
    if (fighter.readyAt > _tick) return;
    final fight = npc.def.fight!;
    final gear = _gearOf(player);
    final damage = _rules.swing(
      _random,
      attack: (level: fight.attack, bonus: 0),
      strength: (level: fight.strength, bonus: 0),
      defence: (
        level: _boosted(player, CombatStat.defence),
        bonus: gear.defence,
      ),
      edge: _rules.edge(fight.style, gear.style),
    );
    fighter
      ..readyAt = _tick + fight.speed
      ..lastFight = _tick;
    target.lastFight = _tick;
    _services.hosting.tell(
      player,
      Hit(target: _movement.standing(player)!.networkId, damage: damage),
    );
    target.health = max(0, target.health - damage);
    _write(_movement.actorOf(player)!, target);
    if (target.health == 0) _die(player, target);
  }

  void _die(String player, _Fighter fighter) {
    _dropOpponentsOf(player);
    fighter
      ..health = fighter.max
      ..lastFight = _tick;
    final actor = _movement.actorOf(player)!;
    _registry.actions.submit(actor, _revive, () {});
    _movement.teleport(player, _spawn);
    _write(actor, fighter);
    _notice(player, 'Oh dear, you are dead.');
  }

  void _dropOpponentsOf(String player) {
    final fighting = [
      for (final MapEntry(key: npc, value: opponent) in _opponent.entries)
        if (opponent == player) npc,
    ];
    fighting.forEach(_drop);
  }

  void _drop(PlacedNpc npc) {
    _opponent.remove(npc);
    npc.release();
  }

  void _heal(int entity, _Fighter fighter) {
    if (fighter.health >= fighter.max || _recently(fighter)) return;
    fighter.health++;
    _write(entity, fighter);
  }
}
