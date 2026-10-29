import 'dart:math';

import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

/// The items of scape_core's test support, which `ItemSupport` in
/// Scape.Core.Tests holds too. Coins and arrows stack and logs do not. The
/// sword, the helmet and the arrows each fit one equipment slot.
final ItemSet _items = ItemSet(
  skills: SkillSet(
    curve: XpCurve(quadratic: 30, cubic: 4),
    skills: [SkillDef(id: 'fighting', name: 'Fighting', levelCap: 50)],
  ),
  items: [
    ItemDef(id: 1, name: 'Coins', stackable: true, value: 1),
    ItemDef(id: 2, name: 'Logs', value: 4),
    ItemDef(
      id: 3,
      name: 'Sword',
      equipSlot: 'weapon',
      needs: (skill: 'fighting', level: 5),
    ),
    ItemDef(id: 4, name: 'Helmet', equipSlot: 'head'),
    ItemDef(id: 5, name: 'Arrows', stackable: true, equipSlot: 'weapon'),
  ],
);

/// An id no item has.
const _unknown = 99;

const _texts = ['', 'o', 'LOG', 'oin', 'zzz', 'S', 'arr'];

/// One step of a script: the dice, every container, and the one it acts on.
final class _Turn {
  _Turn(this.random, this.bags, this.at);

  final Random random;
  final List<Container> bags;
  final int at;

  Container get bag => bags[at];

  /// One of the five items, and now and then one no set has.
  int item() => random.nextInt(12) == 0 ? _unknown : 1 + random.nextInt(5);

  /// Small most of the time, on or near the stack limit now and then, and
  /// zero or below for a refusal.
  int count() => switch (random.nextInt(6)) {
    0 => random.nextInt(3) - 1,
    1 => maxStack - random.nextInt(3),
    _ => 1 + random.nextInt(12),
  };

  /// A held slot of [of] three times in four, else any slot or one just
  /// outside.
  int slot(Container of) {
    final held = of.occupied();
    if (held.isNotEmpty && random.nextInt(4) > 0) {
      return held[random.nextInt(held.length)];
    }
    return random.nextInt(of.size + 2) - 1;
  }

  /// An empty slot of [of] half the time, so moves and splits land.
  int space(Container of) {
    final empty = [
      for (var slot = 0; slot < of.size; slot++)
        if (of.at(slot) == null) slot,
    ];
    if (empty.isNotEmpty && random.nextBool()) {
      return empty[random.nextInt(empty.length)];
    }
    return slot(of);
  }

  /// A slot of [of] holding the same item as [from] half the time, so
  /// stacks merge.
  int match(Container of, int from) {
    final item = of.at(from)?.item;
    final same = [
      for (final slot in of.occupied())
        if (slot != from && of.at(slot)!.item == item) slot,
    ];
    if (same.isNotEmpty && random.nextBool()) {
      return same[random.nextInt(same.length)];
    }
    return slot(of);
  }

  /// No tab half the time, else a tab on or just past a bank's.
  int? tab() => random.nextBool() ? null : random.nextInt(bankTabs + 2) - 1;

  int? maybeCount() => random.nextBool() ? null : count();

  int other() => random.nextInt(bags.length);

  List<ItemStack> stacks() => [
    for (var i = random.nextInt(3); i > 0; i--)
      ItemStack(item(), 1 + random.nextInt(30)),
  ];
}

List<int>? _stackRow(ItemStack? stack) =>
    stack == null ? null : [stack.item, stack.count];

List<List<int>> _stackRows(List<ItemStack> stacks) => [
  for (final stack in stacks) [stack.item, stack.count],
];

_Json _add(_Turn turn) {
  final item = turn.item();
  final count = turn.count();
  final tab = turn.tab();
  return {
    'item': item,
    'count': count,
    'tab': tab,
    'result': attempt(() => turn.bag.add(item, count, tab: tab)),
  };
}

_Json _take(_Turn turn) {
  final slot = turn.slot(turn.bag);
  final count = turn.maybeCount();
  return {
    'slot': slot,
    'count': count,
    'result': _stackRow(turn.bag.take(slot, count)),
  };
}

_Json _move(_Turn turn) {
  final from = turn.slot(turn.bag);
  final to = turn.space(turn.bag);
  return {'from': from, 'to': to, 'result': turn.bag.move(from, to).name};
}

_Json _swap(_Turn turn) {
  final a = turn.slot(turn.bag);
  final b = turn.slot(turn.bag);
  return {'a': a, 'b': b, 'result': turn.bag.swap(a, b).name};
}

_Json _swapWith(_Turn turn) {
  final other = turn.other();
  final slot = turn.slot(turn.bag);
  final otherSlot = turn.slot(turn.bags[other]);
  final outcome = turn.bag.swapWith(turn.bags[other], slot, otherSlot);
  return {
    'other': other,
    'slot': slot,
    'otherSlot': otherSlot,
    'result': outcome.name,
  };
}

_Json _stack(_Turn turn) {
  final from = turn.slot(turn.bag);
  final to = turn.match(turn.bag, from);
  return {'from': from, 'to': to, 'result': turn.bag.stack(from, to).name};
}

_Json _split(_Turn turn) {
  final from = turn.slot(turn.bag);
  final to = turn.space(turn.bag);
  final count = turn.count();
  return {
    'from': from,
    'to': to,
    'count': count,
    'result': turn.bag.split(from, to, count).name,
  };
}

_Json _transfer(_Turn turn) {
  final into = turn.other();
  final slot = turn.slot(turn.bag);
  final count = turn.maybeCount();
  final tab = turn.tab();
  final outcome = turn.bag.transfer(
    slot,
    turn.bags[into],
    count: count,
    tab: tab,
  );
  return {
    'into': into,
    'slot': slot,
    'count': count,
    'tab': tab,
    'result': outcome.name,
  };
}

_Json _exchange(_Turn turn) {
  final take = turn.stacks();
  final give = turn.stacks();
  return {
    'take': _stackRows(take),
    'give': _stackRows(give),
    'result': attempt(() => turn.bag.exchange(take: take, give: give).name),
  };
}

_Json _tryExchange(_Turn turn) {
  final take = turn.stacks();
  final give = turn.stacks();
  return {
    'take': _stackRows(take),
    'give': _stackRows(give),
    'result': attempt(() => turn.bag.tryExchange(take: take, give: give).name),
  };
}

_Json _canHold(_Turn turn) {
  final item = turn.item();
  return {'item': item, 'result': attempt(() => turn.bag.canHold(item))};
}

_Json _countOf(_Turn turn) {
  final item = turn.item();
  return {'item': item, 'result': turn.bag.countOf(item)};
}

_Json _search(_Turn turn) {
  final text = _texts[turn.random.nextInt(_texts.length)];
  final tab = turn.tab();
  return {
    'text': text,
    'tab': tab,
    'result': turn.bag.search(text, tab: tab),
  };
}

_Json _markClean(_Turn turn) {
  turn.bag.markClean();
  return {'result': null};
}

/// A client's copy takes what it is told, so the stack may break the
/// container's own rules.
_Json _mirror(_Turn turn) {
  final slot = turn.slot(turn.bag);
  final stack = turn.random.nextBool()
      ? null
      : ItemStack(1 + turn.random.nextInt(5), 1 + turn.random.nextInt(5));
  turn.bag.mirror([(slot: slot, stack: stack)]);
  return {'slot': slot, 'stack': _stackRow(stack), 'result': null};
}

/// Every step a script may take. Adding comes up three times as often, so the
/// containers fill.
const _menu = <(String, _Json Function(_Turn))>[
  ('add', _add),
  ('add', _add),
  ('add', _add),
  ('take', _take),
  ('move', _move),
  ('swap', _swap),
  ('swapWith', _swapWith),
  ('stack', _stack),
  ('split', _split),
  ('transfer', _transfer),
  ('exchange', _exchange),
  ('tryExchange', _tryExchange),
  ('canHold', _canHold),
  ('countOf', _countOf),
  ('search', _search),
  ('markClean', _markClean),
  ('mirror', _mirror),
];

_Json _bagRow(Container bag) => {
  'slots': [
    for (final slot in bag.occupied()) [slot, ...?_stackRow(bag.at(slot))],
  ],
  'dirty': bag.dirty,
};

List<Container> _bags() => [
  for (final id in ContainerId.values) Container.forId(id, _items),
];

/// Eighty random steps over one player's three containers. Each step's
/// arguments travel in the fixture, and its result and the end state are the
/// expectation.
_Json _script(Random random) {
  final bags = _bags();
  final steps = <_Json>[];
  final results = <Object?>[];
  for (var i = 0; i < 80; i++) {
    final (op, act) = _menu[random.nextInt(_menu.length)];
    final at = random.nextInt(bags.length);
    final step = act(_Turn(random, bags, at));
    results.add(step.remove('result'));
    steps.add({'op': op, 'in': at, ...step});
  }
  return {
    'op': 'script',
    'steps': steps,
    'expect': {
      'results': results,
      'bags': [for (final bag in bags) _bagRow(bag)],
    },
  };
}

/// A saved list for one container, with gaps, unknown items, piles of items
/// that do not stack, items in slots that refuse them, and for the smaller
/// containers stacks past the last slot.
_Json _restore(Random random) {
  final at = random.nextInt(ContainerId.values.length);
  final turn = _Turn(random, _bags(), at);
  final bag = turn.bag;
  final saved = [
    for (var i = random.nextInt(min(bag.size, 30) + 3); i > 0; i--)
      random.nextInt(3) == 0
          ? null
          : ItemStack(
              turn.item(),
              random.nextBool() ? 1 : 1 + random.nextInt(maxStack),
            ),
  ];
  final lost = bag.restore(saved);
  return {
    'op': 'restore',
    'in': at,
    'saved': [for (final stack in saved) _stackRow(stack)],
    'expect': {'lost': lost, ..._bagRow(bag)},
  };
}

void main(List<String> args) {
  final random = Random(7);
  writeFixture(
    root: args.single,
    package: 'scape_core',
    name: 'containers',
    cases: [
      for (var i = 0; i < 30; i++) _script(random),
      for (var i = 0; i < 40; i++) _restore(random),
    ],
  );
}
