import 'dart:math';

/// The name of the replicated component on a fighter.
const String healthName = 'Health';

/// Ints in that component: the hit points now, then the most there can be.
const int healthInts = 2;

/// The most tiles a shot or a spell may reach. A flight of that many tiles
/// still fits the byte a [Projectile] counts its ticks in.
const int maxRange = 255;

/// A stat as a fight sees it: a skill level and the bonus worn gear adds.
typedef Rating = ({int level, int bonus});

/// How an attack is made. Each style beats one other and loses to the one
/// that beats it.
enum CombatStyle {
  melee,
  ranged,
  magic;

  /// The style this one has the edge over.
  CombatStyle get beats => switch (this) {
    melee => magic,
    ranged => melee,
    magic => ranged,
  };
}

/// What a worn item adds to a fight. A weapon also names how many ticks pass
/// between its swings. A ranged weapon names how far it shoots and the kind of
/// ammo it fires, and ammo names the kind it is.
final class CombatBonus {
  CombatBonus({
    this.attack = 0,
    this.strength = 0,
    this.defence = 0,
    this.speed,
    this.style = CombatStyle.melee,
    this.range,
    this.ammo,
  }) {
    if (attack < 0 || strength < 0 || defence < 0) {
      throw ArgumentError('A bonus is not negative.');
    }
    if (speed != null && speed! < 1) {
      throw ArgumentError.value(speed, 'speed', 'is below one');
    }
    _checkRanged(style, range, ammo);
  }

  final int attack;
  final int strength;
  final int defence;

  /// Ticks between swings, or null for an item that is not a weapon.
  final int? speed;

  /// Gear is melee or ranged. Magic comes from spells.
  final CombatStyle style;

  /// Tiles a ranged weapon shoots, or null for any other item.
  final int? range;

  /// The kind of ammo a ranged weapon fires, or the kind an arrow or bolt is.
  final String? ammo;
}

void _checkRanged(CombatStyle style, int? range, String? ammo) {
  if (style == CombatStyle.magic) {
    throw ArgumentError.value(style, 'style', 'is not worn');
  }
  if (style == CombatStyle.ranged) {
    if (range == null || range < 1 || range > maxRange || ammo == null) {
      throw ArgumentError('A ranged weapon has a range and fires ammo.');
    }
  } else if (range != null) {
    throw ArgumentError.value(
      range,
      'range',
      'is on a weapon that is not ranged',
    );
  }
}

/// How a creature fights. Its levels stand in for the skill and gear a
/// player brings.
final class NpcFight {
  NpcFight({
    required this.health,
    this.attack = 1,
    this.strength = 1,
    this.defence = 1,
    this.speed = 4,
    this.respawnTicks = 50,
    this.style = CombatStyle.melee,
  }) {
    if (health < 1) throw ArgumentError.value(health, 'health', 'is below one');
    if (attack < 1 || strength < 1 || defence < 1) {
      throw ArgumentError('A level is at least one.');
    }
    if (speed < 1) throw ArgumentError.value(speed, 'speed', 'is below one');
    if (respawnTicks < 1) {
      throw ArgumentError.value(respawnTicks, 'respawnTicks', 'is below one');
    }
  }

  final int health;
  final int attack;
  final int strength;
  final int defence;

  /// Ticks between swings.
  final int speed;

  /// Ticks from death to the creature standing at home again.
  final int respawnTicks;

  /// How it fights, and so which attacks have the edge over it.
  final CombatStyle style;
}

/// The numbers a fight is worked out from. A roll is `(level + effortBase) *
/// (bonus + gearBase)`. The chance to hit is the attack roll over the sum of
/// both rolls, held between [minHitChance] and [maxHitChance]. A hit does
/// from one to the maximum, which is one plus the strength roll over
/// [hitDivisor]. An attack in a style that beats the defender's rolls
/// [styleEdge] times as high, and one in a style that loses rolls that much
/// lower.
final class CombatRules {
  CombatRules({
    required this.fightingSkill,
    required this.healthSkill,
    this.effortBase = 8,
    this.gearBase = 64,
    this.hitDivisor = 320,
    this.minHitChance = 0.05,
    this.maxHitChance = 0.95,
    this.baseHealth = 9,
    this.unarmedSpeed = 4,
    this.combatTicks = 8,
    this.regenTicks = 100,
    this.fightingXpPerHealth = 4,
    this.healthXpPerHealth = 1,
    this.styleEdge = 1.25,
    this.projectileSpeed = 3,
  }) {
    if (fightingSkill.isEmpty || healthSkill.isEmpty) {
      throw ArgumentError('A fight needs a fighting and a health skill.');
    }
    if (effortBase < 0 || gearBase < 1 || hitDivisor < 1) {
      throw ArgumentError('A roll needs a positive gear base and divisor.');
    }
    if (minHitChance <= 0 || minHitChance > maxHitChance || maxHitChance >= 1) {
      throw ArgumentError('Hit chances must satisfy 0 < min <= max < 1.');
    }
    if (baseHealth < 1 || unarmedSpeed < 1 || combatTicks < 0) {
      throw ArgumentError('Health, speed and the combat timer are positive.');
    }
    if (regenTicks < 1 || fightingXpPerHealth < 0 || healthXpPerHealth < 0) {
      throw ArgumentError('Regeneration and experience are not negative.');
    }
    if (styleEdge < 1 || projectileSpeed < 1) {
      throw ArgumentError('A style edge is one or more and a shot moves.');
    }
  }

  final String fightingSkill;
  final String healthSkill;
  final int effortBase;
  final int gearBase;
  final int hitDivisor;
  final double minHitChance;
  final double maxHitChance;
  final int baseHealth;

  /// Ticks between swings with no weapon.
  final int unarmedSpeed;

  /// Ticks after a swing in which a fighter does not regenerate.
  final int combatTicks;

  /// Ticks between each hit point regained.
  final int regenTicks;

  /// Experience in the fighting skill for each hit point a creature had.
  final int fightingXpPerHealth;

  /// Experience in the health skill for each hit point a creature had.
  final int healthXpPerHealth;

  /// How much a style that beats another multiplies the attack roll.
  final double styleEdge;

  /// Tiles a projectile crosses each tick.
  final int projectileSpeed;

  /// The hit points a fighter with [healthLevel] has when whole.
  int maxHealth(int healthLevel) => baseHealth + healthLevel;

  int _roll(Rating rating) =>
      (rating.level + effortBase) * (rating.bonus + gearBase);

  /// Ticks a projectile flies over [distance] tiles, at least one.
  int flightTicks(int distance) =>
      max(1, (distance + projectileSpeed - 1) ~/ projectileSpeed);

  /// What an attack in style [from] multiplies its roll by against [to].
  double edge(CombatStyle from, CombatStyle to) {
    if (from.beats == to) return styleEdge;
    return to.beats == from ? 1 / styleEdge : 1;
  }

  /// The chance that a swing by [attack] lands against [defence].
  double hitChance(Rating attack, Rating defence, {double edge = 1}) {
    final attackRoll = _roll(attack) * edge;
    final chance = attackRoll / (attackRoll + _roll(defence));
    return chance.clamp(minHitChance, maxHitChance);
  }

  /// The most one hit by [strength] does.
  int maxHit(Rating strength) => 1 + _roll(strength) ~/ hitDivisor;

  /// The damage of one swing: zero for a miss.
  int swing(
    Random random, {
    required Rating attack,
    required Rating strength,
    required Rating defence,
    double edge = 1,
  }) => damage(
    random,
    most: maxHit(strength),
    attack: attack,
    defence: defence,
    edge: edge,
  );

  /// The damage of one attack that does up to [most]: zero for a miss.
  // F5: an attack answers with its damage and spends the dice it is given.
  int damage(
    Random random, {
    required int most,
    required Rating attack,
    required Rating defence,
    double edge = 1,
  }) {
    if (random.nextDouble() >= hitChance(attack, defence, edge: edge)) return 0;
    return 1 + random.nextInt(most);
  }
}
