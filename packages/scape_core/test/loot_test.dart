import 'dart:math';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

/// Picks the numbers it was given, in order, and repeats the last.
final class _Fixed implements Random {
  _Fixed(this._values);

  final List<int> _values;
  int _at = 0;

  @override
  int nextInt(int max) {
    final value = _values[_at < _values.length - 1 ? _at++ : _at];
    return value % max;
  }

  @override
  double nextDouble() => 0;

  @override
  bool nextBool() => false;
}

Map<int, int> sample(LootTable table, int rolls, {int seed = 7}) {
  final random = Random(seed);
  final totals = <int, int>{};
  for (var i = 0; i < rolls; i++) {
    for (final stack in table.roll(random)) {
      totals[stack.item] = (totals[stack.item] ?? 0) + stack.count;
    }
  }
  return totals;
}

void main() {
  group('rolling', () {
    test('always pays what it always pays', () {
      final table = LootTable(always: [ItemStack(logs, 2)]);
      expect(table.roll(Random(1)), [ItemStack(logs, 2)]);
    });

    test('an empty table pays nothing', () {
      expect(LootTable().roll(Random(1)), isEmpty);
    });

    test('picks the entry the roll lands in', () {
      final table = LootTable(
        entries: [
          LootItem(weight: 3, item: logs),
          LootItem(weight: 1, item: coins),
        ],
      );
      expect(table.roll(_Fixed([2])), [ItemStack(logs, 1)]);
      expect(table.roll(_Fixed([3])), [ItemStack(coins, 1)]);
    });

    test('the weight of nothing is a roll that pays no entry', () {
      final table = LootTable(
        entries: [LootItem(weight: 1, item: logs)],
        nothing: 2,
      );
      expect(table.roll(_Fixed([0])), [ItemStack(logs, 1)]);
      expect(table.roll(_Fixed([1])), isEmpty);
      expect(table.roll(_Fixed([2])), isEmpty);
    });

    test('a count runs from its fewest to its most', () {
      final table = LootTable(
        entries: [LootItem(weight: 1, item: coins, min: 5, max: 9)],
      );
      expect(table.roll(_Fixed([0, 0])).single.count, 5);
      expect(table.roll(_Fixed([0, 4])).single.count, 9);
    });

    test('an entry can be another table, which rolls whole', () {
      final inner = LootTable(
        always: [ItemStack(logs, 1)],
        entries: [LootItem(weight: 1, item: coins)],
      );
      final table = LootTable(entries: [LootSub(weight: 1, table: inner)]);
      expect(table.roll(_Fixed([0])), [
        ItemStack(logs, 1),
        ItemStack(coins, 1),
      ]);
    });

    test(
      'the rare table pays once in so many rolls and on top of the pick',
      () {
        final table = LootTable(
          entries: [LootItem(weight: 1, item: logs)],
          rare: LootTable(always: [ItemStack(sword, 1)]),
          rareOneIn: 50,
        );
        expect(table.roll(_Fixed([0])), [
          ItemStack(sword, 1),
          ItemStack(logs, 1),
        ]);
        expect(table.roll(_Fixed([1])), [ItemStack(logs, 1)]);
      },
    );
  });

  group('over many rolls', () {
    test('what is paid converges on the weights', () {
      final table = LootTable(
        entries: [
          LootItem(weight: 6, item: logs),
          LootItem(weight: 3, item: coins),
          LootItem(weight: 1, item: sword),
        ],
      );
      const rolls = 20000;
      final totals = sample(table, rolls);
      expect(totals[logs]! / rolls, closeTo(0.6, 0.02));
      expect(totals[coins]! / rolls, closeTo(0.3, 0.02));
      expect(totals[sword]! / rolls, closeTo(0.1, 0.02));
    });

    test('the rare tier comes up about once in rareOneIn rolls', () {
      final table = LootTable(
        rare: LootTable(always: [ItemStack(sword, 1)]),
        rareOneIn: 20,
      );
      const rolls = 20000;
      expect(sample(table, rolls)[sword]! / rolls, closeTo(0.05, 0.01));
    });

    test('nothing takes its share of the rolls', () {
      final table = LootTable(
        entries: [LootItem(weight: 1, item: logs)],
        nothing: 3,
      );
      const rolls = 20000;
      expect(sample(table, rolls)[logs]! / rolls, closeTo(0.25, 0.02));
    });
  });

  group('items', () {
    test('lists every item the table can pay, tables inside tables too', () {
      final table = LootTable(
        always: [ItemStack(logs, 1)],
        entries: [
          LootItem(weight: 1, item: coins),
          LootSub(
            weight: 1,
            table: LootTable(entries: [LootItem(weight: 1, item: helmet)]),
          ),
        ],
        rare: LootTable(always: [ItemStack(sword, 1)]),
        rareOneIn: 10,
      );
      expect(table.items.toSet(), {logs, coins, helmet, sword});
    });
  });

  group('building', () {
    test('refuses a weight below one', () {
      expect(() => LootItem(weight: 0, item: logs), throwsArgumentError);
    });

    test('refuses a count below one or a most below the fewest', () {
      expect(
        () => LootItem(weight: 1, item: logs, min: 0),
        throwsArgumentError,
      );
      expect(
        () => LootItem(weight: 1, item: logs, min: 3, max: 2),
        throwsArgumentError,
      );
    });

    test('refuses a rare tier that always pays or never does', () {
      final rare = LootTable();
      expect(() => LootTable(rare: rare, rareOneIn: 1), throwsArgumentError);
      expect(() => LootTable(rare: rare), throwsArgumentError);
      expect(() => LootTable(rareOneIn: 5), throwsArgumentError);
    });

    test('refuses a negative weight of nothing', () {
      expect(() => LootTable(nothing: -1), throwsArgumentError);
    });
  });
}
