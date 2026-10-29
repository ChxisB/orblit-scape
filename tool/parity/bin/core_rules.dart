import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

/// The edges a style gives: none, a win and a loss.
const _edges = [1.0, 1.25, 1 / 1.25];

/// Values that sit on or just past a bound of an Anim or Look field.
const _seqEdges = [-1, 0, maxAnimSeq, maxAnimSeq + 1];
const _lookEdges = [-1, 0, maxItemId + 1, 0x1000000];

/// A seeded source that writes down every draw. Dart and C# seed a random
/// source differently, so the dice travel in the fixture and C# replays them.
final class _Recorder implements Random {
  _Recorder(this._source);

  final Random _source;
  final List<_Json> draws = [];

  @override
  int nextInt(int max) {
    final value = _source.nextInt(max);
    draws.add({'below': max, 'gave': value});
    return value;
  }

  @override
  double nextDouble() {
    final value = _source.nextDouble();
    draws.add({'gave': number(value)});
    return value;
  }

  @override
  bool nextBool() => nextInt(2) == 1;
}

_Json _curve(Random random) {
  final curve = XpCurve(
    quadratic: 1 + random.nextInt(60),
    cubic: random.nextInt(9),
  );
  final cap = 1 + random.nextInt(maxLevel);
  final xps = [
    for (var i = 0; i < 8; i++)
      curve.xpForLevel(1 + random.nextInt(cap)) + random.nextInt(3) - 1,
  ];
  return {
    'op': 'curve',
    'quadratic': curve.quadratic,
    'cubic': curve.cubic,
    'cap': cap,
    'xps': xps,
    'expect': {
      'costs': [for (var level = 1; level <= cap; level++) curve.xpForLevel(level)],
      'levels': [for (final xp in xps) curve.levelOf(xp, cap)],
    },
  };
}

CombatRules _rules(Random random) => CombatRules(
  fightingSkill: 'fighting',
  healthSkill: 'health',
  effortBase: random.nextInt(17),
  gearBase: 1 + random.nextInt(128),
  hitDivisor: 1 + random.nextInt(640),
);

_Json _rulesRow(CombatRules rules) => {
  'effortBase': rules.effortBase,
  'gearBase': rules.gearBase,
  'hitDivisor': rules.hitDivisor,
};

Rating _rating(Random random) =>
    (level: random.nextInt(151), bonus: random.nextInt(401));

List<int> _ratingRow(Rating rating) => [rating.level, rating.bonus];

_Json _hit(Random random) {
  final rules = _rules(random);
  final attack = _rating(random);
  final defence = _rating(random);
  final strength = _rating(random);
  final edge = _edges[random.nextInt(_edges.length)];
  return {
    'op': 'hit',
    'rules': _rulesRow(rules),
    'attack': _ratingRow(attack),
    'defence': _ratingRow(defence),
    'strength': _ratingRow(strength),
    'edge': edge,
    'expect': {
      'chance': number(rules.hitChance(attack, defence, edge: edge)),
      'most': rules.maxHit(strength),
    },
  };
}

_Json _swing(Random random) {
  final rules = _rules(random);
  final attack = _rating(random);
  final strength = _rating(random);
  final defence = _rating(random);
  final edge = _edges[random.nextInt(_edges.length)];
  final dice = _Recorder(random);
  final damage = rules.swing(
    dice,
    attack: attack,
    strength: strength,
    defence: defence,
    edge: edge,
  );
  return {
    'op': 'swing',
    'rules': _rulesRow(rules),
    'attack': _ratingRow(attack),
    'strength': _ratingRow(strength),
    'defence': _ratingRow(defence),
    'edge': edge,
    'dice': dice.draws,
    'expect': damage,
  };
}

/// A random table that nests other tables up to [depth] deep.
LootTable _table(Random random, int depth) {
  final rare = depth > 0 && random.nextInt(3) == 0;
  return LootTable(
    always: [
      for (var i = random.nextInt(2); i > 0; i--)
        ItemStack(1 + random.nextInt(9), 1 + random.nextInt(5)),
    ],
    entries: [for (var i = random.nextInt(4); i > 0; i--) _entry(random, depth)],
    nothing: random.nextInt(3) == 0 ? random.nextInt(5) : 0,
    rare: rare ? _table(random, depth - 1) : null,
    rareOneIn: rare ? 2 + random.nextInt(9) : 0,
  );
}

LootEntry _entry(Random random, int depth) {
  final weight = 1 + random.nextInt(10);
  if (depth > 0 && random.nextInt(3) == 0) {
    return LootSub(weight: weight, table: _table(random, depth - 1));
  }
  final min = 1 + random.nextInt(5);
  return LootItem(
    weight: weight,
    item: 1 + random.nextInt(9),
    min: min,
    max: min + random.nextInt(4),
  );
}

_Json _tableRow(LootTable table) => {
  'always': [for (final stack in table.always) _stackRow(stack)],
  'entries': [for (final entry in table.entries) _entryRow(entry)],
  'nothing': table.nothing,
  'rare': switch (table.rare) {
    final rare? => _tableRow(rare),
    null => null,
  },
  'rareOneIn': table.rareOneIn,
};

_Json _entryRow(LootEntry entry) => switch (entry) {
  LootItem(:final weight, :final item, :final min, :final max) => {
    'weight': weight,
    'item': item,
    'min': min,
    'max': max,
  },
  LootSub(:final weight, :final table) => {
    'weight': weight,
    'table': _tableRow(table),
  },
};

List<int> _stackRow(ItemStack stack) => [stack.item, stack.count];

/// Four rolls of one random table, all on one list of dice.
_Json _loot(Random random) {
  final table = _table(random, 2);
  final dice = _Recorder(random);
  final rolls = [
    for (var i = 0; i < 4; i++)
      [for (final stack in table.roll(dice)) _stackRow(stack)],
  ];
  return {
    'op': 'loot',
    'table': _tableRow(table),
    'dice': dice.draws,
    'expect': rolls,
  };
}

/// Every resting state with no act and with each act.
List<_Json> _anims(Random random) => [
  for (final base in ActorState.values.where((state) => !state.isAct))
    for (final act in [null, ...ActorState.values.where((state) => state.isAct)])
      _anim(Anim(base: base, act: act, seq: random.nextInt(maxAnimSeq + 1))),
];

_Json _anim(Anim anim) {
  final bytes = Uint8List(animInts * 4);
  writeAnim(bytes, anim);
  return {
    'op': 'anim',
    'base': anim.base.name,
    'act': anim.act?.name,
    'seq': anim.seq,
    'expect': hex(bytes),
  };
}

Uint8List _intBytes(List<int> ints) {
  final bytes = Uint8List(ints.length * 4);
  final data = ByteData.sublistView(bytes);
  for (final (index, value) in ints.indexed) {
    data.setInt32(index * 4, value, Endian.little);
  }
  return bytes;
}

/// Ints that name a state, an act or neither, and a count on or near its
/// bounds half the time.
_Json _animRead(Random random) {
  final seq = random.nextBool()
      ? _seqEdges[random.nextInt(_seqEdges.length)]
      : random.nextInt(maxAnimSeq);
  final ints = [random.nextInt(10) - 1, random.nextInt(10) - 2, seq];
  final anim = tryReadAnim(_intBytes(ints));
  return {
    'op': 'animRead',
    'ints': ints,
    'expect': anim == null ? null : [anim.base.name, anim.act?.name, anim.seq],
  };
}

Look _look(Random random) => Look(
  skin: random.nextInt(0x1000000),
  hair: random.nextInt(0x1000000),
  cloth: random.nextInt(0x1000000),
  worn: [
    for (final _ in equipmentSlots)
      random.nextBool() ? 0 : 1 + random.nextInt(maxItemId),
  ],
);

List<int> _lookInts(Look look) => [
  look.skin,
  look.hair,
  look.cloth,
  ...look.worn,
];

_Json _lookWrite(Random random) {
  final look = _look(random);
  final bytes = Uint8List(lookInts * 4);
  writeLook(bytes, look);
  return {'op': 'look', 'ints': _lookInts(look), 'expect': hex(bytes)};
}

/// A good look with one int moved onto or past a bound.
_Json _lookRead(Random random) {
  final ints = _lookInts(_look(random));
  ints[random.nextInt(lookInts)] = _lookEdges[random.nextInt(_lookEdges.length)];
  final look = tryReadLook(_intBytes(ints));
  return {
    'op': 'lookRead',
    'ints': ints,
    'expect': look == null ? null : _lookInts(look),
  };
}

void main(List<String> args) {
  final random = Random(7);
  writeFixture(
    root: args.single,
    package: 'scape_core',
    name: 'rules',
    cases: [
      for (var i = 0; i < 12; i++) _curve(random),
      for (var i = 0; i < 40; i++) _hit(random),
      for (var i = 0; i < 60; i++) _swing(random),
      for (var i = 0; i < 30; i++) _loot(random),
      ..._anims(random),
      for (var i = 0; i < 40; i++) _animRead(random),
      for (var i = 0; i < 12; i++) _lookWrite(random),
      for (var i = 0; i < 30; i++) _lookRead(random),
    ],
  );
}
