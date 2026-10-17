import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

Container bag(ContainerId id) => Container.forId(id, testItems());

Container inventory() => bag(ContainerId.inventory);

void main() {
  group('add', () {
    test('puts a stackable item on its own stack before an empty slot', () {
      final c = inventory();
      expect(c.add(coins, 10), 10);
      expect(c.add(coins, 5), 5);
      expect(c.at(0), ItemStack(coins, 15));
      expect(c.at(1), isNull);
    });

    test('gives a non-stackable item a slot each', () {
      final c = inventory();
      expect(c.add(logs, 3), 3);
      expect([for (var i = 0; i < 4; i++) c.at(i)?.count], [1, 1, 1, null]);
    });

    test('returns how many fit when the container is full', () {
      final c = inventory();
      expect(c.add(logs, inventorySize + 5), inventorySize);
      expect(c.add(logs, 1), 0);
      expect(c.add(coins, 1), 0);
    });

    test('tops up a stack in a later slot while a full one is skipped', () {
      final c = inventory();
      c.add(coins, maxStack);
      c.add(logs, 1);
      c.add(coins, 7);
      expect(c.at(0), ItemStack(coins, maxStack));
      expect(c.countOf(coins), maxStack + 7);
    });

    test('refuses more on a stack at the integer limit', () {
      final c = Container(items: testItems(), size: 1);
      expect(c.add(coins, maxStack), maxStack);
      expect(c.add(coins, 1), 0);
      expect(c.at(0), ItemStack(coins, maxStack));
    });

    test('stops at the limit and starts a new stack with the rest', () {
      final c = inventory();
      expect(c.add(coins, maxStack), maxStack);
      expect(c.add(coins, 10), 10);
      expect(c.at(1), ItemStack(coins, 10));
    });

    test('fills the container with stacks when asked for more than fit', () {
      final c = Container(items: testItems(), size: 2);
      expect(c.add(coins, maxStack * 2 + 5), maxStack * 2);
    });

    test('throws for an unknown item and a count below one', () {
      final c = inventory();
      expect(() => c.add(99, 1), throwsArgumentError);
      expect(() => c.add(coins, 0), throwsRangeError);
    });

    test('puts a worn item only in its own slot', () {
      final c = bag(ContainerId.equipment);
      expect(c.add(helmet, 2), 1);
      expect(c.at(equipmentSlots.indexOf('head')), ItemStack(helmet, 1));
      expect(c.add(logs, 1), 0);
    });

    test('keeps to one tab and says no for a tab that is not there', () {
      final c = bag(ContainerId.bank);
      expect(c.add(coins, 5, tab: 1), 5);
      expect(c.at(bankTabSize), ItemStack(coins, 5));
      expect(c.at(0), isNull);
      expect(c.add(coins, 5, tab: bankTabs), 0);
      expect(c.add(coins, 5, tab: -1), 0);
      expect(c.add(coins, 5, tab: 0), 5);
      expect(c.at(0), ItemStack(coins, 5));
    });

    test('a full tab refuses though another has room', () {
      final c = bag(ContainerId.bank);
      expect(c.add(logs, bankTabSize + 3, tab: 0), bankTabSize);
      expect(c.add(logs, 1, tab: 1), 1);
    });
  });

  group('canHold', () {
    test('is true for an empty container and false for a full one', () {
      final c = inventory();
      expect(c.canHold(logs), isTrue);

      c.add(logs, inventorySize);

      expect(c.canHold(logs), isFalse);
      expect(c.canHold(coins), isFalse);
    });

    test('is true for a stackable item that has a stack with room', () {
      final c = inventory()..add(coins, 5);
      c.add(logs, inventorySize - 1);

      expect(c.canHold(coins), isTrue);
    });

    test('is false for a stack at the limit when no slot is free', () {
      final c = inventory()..add(coins, maxStack);
      c.add(logs, inventorySize - 1);

      expect(c.canHold(coins), isFalse);
    });

    test('honours where an item may go', () {
      final worn = bag(ContainerId.equipment);

      expect(worn.canHold(helmet), isTrue);
      expect(worn.canHold(logs), isFalse);
      expect(() => worn.canHold(99), throwsArgumentError);
    });
  });

  group('take', () {
    test('takes part of a stack, all of it, and nothing from empty', () {
      final c = inventory()..add(coins, 10);
      expect(c.take(0, 4), ItemStack(coins, 6 - 2));
      expect(c.at(0), ItemStack(coins, 6));
      expect(c.take(0, 99), ItemStack(coins, 6));
      expect(c.at(0), isNull);
      expect(c.take(0), isNull);
      expect(c.take(-1), isNull);
      expect(c.take(inventorySize), isNull);
    });

    test('takes the whole stack with no count and nothing for zero', () {
      final c = inventory()..add(coins, 10);
      expect(c.take(0, 0), isNull);
      expect(c.take(0, -3), isNull);
      expect(c.take(0), ItemStack(coins, 10));
    });
  });

  group('move', () {
    test('moves a stack to an empty slot', () {
      final c = inventory()..add(coins, 3);
      expect(c.move(0, 5), Outcome.done);
      expect(c.at(0), isNull);
      expect(c.at(5), ItemStack(coins, 3));
    });

    test('refuses an empty source, a full target and a missing slot', () {
      final c = inventory()
        ..add(coins, 3)
        ..add(logs, 1);
      expect(c.move(4, 5), Outcome.nothingThere);
      expect(c.move(0, 1), Outcome.wontFit);
      expect(c.move(0, 0), Outcome.wontFit);
      expect(c.move(-1, 2), Outcome.noSuchSlot);
      expect(c.move(0, inventorySize), Outcome.noSuchSlot);
      expect(c.at(0), ItemStack(coins, 3));
    });

    test('refuses a slot the item may not sit in', () {
      final c = bag(ContainerId.equipment)..add(helmet, 1);
      expect(c.move(0, 1), Outcome.notAllowed);
      expect(c.at(0), ItemStack(helmet, 1));
    });
  });

  group('swap', () {
    test('exchanges two stacks, and moves one into an empty slot', () {
      final c = inventory()
        ..add(coins, 3)
        ..add(logs, 1);
      expect(c.swap(0, 1), Outcome.done);
      expect(c.at(0), ItemStack(logs, 1));
      expect(c.at(1), ItemStack(coins, 3));
      expect(c.swap(1, 9), Outcome.done);
      expect(c.at(9), ItemStack(coins, 3));
      expect(c.at(1), isNull);
    });

    test('refuses two empty slots and a missing slot', () {
      final c = inventory();
      expect(c.swap(0, 1), Outcome.nothingThere);
      expect(c.swap(0, inventorySize), Outcome.noSuchSlot);
    });

    test('is no change for a slot with itself', () {
      final c = inventory()
        ..add(coins, 3)
        ..markClean();
      expect(c.swap(0, 0), Outcome.done);
      expect(c.dirty, isEmpty);
    });

    test('refuses when either item would land in a slot it may not', () {
      final c = bag(ContainerId.equipment)..add(helmet, 1);
      expect(c.swap(0, 3), Outcome.notAllowed);
      expect(c.swap(3, 0), Outcome.notAllowed);
      expect(c.at(0), ItemStack(helmet, 1));
    });

    test('checks the item in the other slot too', () {
      final c = bag(ContainerId.equipment)
        ..add(helmet, 1)
        ..add(sword, 1);
      expect(c.swap(0, 3), Outcome.notAllowed);
    });
  });

  group('stack', () {
    test('adds one stack to another and empties the source', () {
      final c = inventory()
        ..add(coins, 7)
        ..split(0, 5, 3);
      expect(c.stack(0, 5), Outcome.done);
      expect(c.at(0), isNull);
      expect(c.at(5), ItemStack(coins, 7));
    });

    test('moves only what fits under the limit', () {
      final c = inventory()
        ..add(coins, maxStack - 2)
        ..mirror([(slot: 1, stack: ItemStack(coins, 5))]);
      expect(c.stack(1, 0), Outcome.partly);
      expect(c.at(0), ItemStack(coins, maxStack));
      expect(c.at(1), ItemStack(coins, 3));
      expect(c.stack(1, 0), Outcome.wontFit);
    });

    test('refuses other items, items that do not stack, itself and gaps', () {
      final c = inventory()
        ..add(coins, 3)
        ..add(logs, 2);
      expect(c.stack(0, 1), Outcome.wontFit);
      expect(c.stack(1, 2), Outcome.wontFit);
      expect(c.stack(0, 0), Outcome.wontFit);
      expect(c.stack(0, 9), Outcome.nothingThere);
      expect(c.stack(9, 0), Outcome.nothingThere);
      expect(c.stack(0, inventorySize), Outcome.noSuchSlot);
    });

    test('refuses two stacks of a non-stackable item', () {
      final c = inventory()..add(logs, 2);
      expect(c.stack(0, 1), Outcome.wontFit);
    });
  });

  group('split', () {
    test('moves part of a stack to an empty slot', () {
      final c = inventory()..add(coins, 10);
      expect(c.split(0, 4, 3), Outcome.done);
      expect(c.at(0), ItemStack(coins, 7));
      expect(c.at(4), ItemStack(coins, 3));
    });

    test('refuses a count of none, the whole stack, or more', () {
      final c = inventory()..add(coins, 10);
      expect(c.split(0, 4, 0), Outcome.wontFit);
      expect(c.split(0, 4, 10), Outcome.wontFit);
      expect(c.split(0, 4, 11), Outcome.wontFit);
      expect(c.split(0, 4, 9), Outcome.done);
    });

    test('refuses a full target, an empty source and a missing slot', () {
      final c = inventory()
        ..add(coins, 10)
        ..add(logs, 1);
      expect(c.split(0, 1, 3), Outcome.wontFit);
      expect(c.split(5, 6, 1), Outcome.nothingThere);
      expect(c.split(0, -1, 1), Outcome.noSuchSlot);
    });

    test('refuses a slot the piece may not sit in', () {
      final c = bag(ContainerId.equipment)..add(arrows, 10);
      expect(c.split(3, 0, 2), Outcome.notAllowed);
      expect(c.at(3), ItemStack(arrows, 10));
    });
  });

  group('transfer', () {
    test('deposits a whole stack and withdraws part of one', () {
      final inv = inventory()..add(coins, 10);
      final bank = bag(ContainerId.bank);
      expect(inv.transfer(0, bank), Outcome.done);
      expect(inv.at(0), isNull);
      expect(bank.at(0), ItemStack(coins, 10));
      expect(bank.transfer(0, inv, count: 4), Outcome.done);
      expect(bank.at(0), ItemStack(coins, 6));
      expect(inv.at(0), ItemStack(coins, 4));
    });

    test('leaves what does not fit in the source', () {
      final inv = inventory()..add(coins, 10);
      final small = Container(items: testItems(), size: 1)
        ..add(coins, maxStack - 3);
      expect(inv.transfer(0, small), Outcome.partly);
      expect(small.at(0), ItemStack(coins, maxStack));
      expect(inv.at(0), ItemStack(coins, 7));
      expect(inv.transfer(0, small), Outcome.wontFit);
      expect(inv.at(0), ItemStack(coins, 7));
    });

    test('puts the deposit in the tab asked for', () {
      final inv = inventory()..add(coins, 10);
      final bank = bag(ContainerId.bank);
      expect(inv.transfer(0, bank, tab: 2), Outcome.done);
      expect(bank.at(2 * bankTabSize), ItemStack(coins, 10));
    });

    test('refuses itself, an empty slot, a missing slot and a zero count', () {
      final inv = inventory()..add(coins, 10);
      final bank = bag(ContainerId.bank);
      expect(inv.transfer(0, inv), Outcome.notAllowed);
      expect(inv.transfer(1, bank), Outcome.nothingThere);
      expect(inv.transfer(-1, bank), Outcome.noSuchSlot);
      expect(inv.transfer(0, bank, count: 0), Outcome.wontFit);
      expect(inv.at(0), ItemStack(coins, 10));
    });

    test('is refused by a container that will not take the item', () {
      final inv = inventory()..add(logs, 1);
      final worn = bag(ContainerId.equipment);
      expect(inv.transfer(0, worn), Outcome.wontFit);
      expect(inv.at(0), ItemStack(logs, 1));
    });
  });

  group('dirty', () {
    test('lists changed slots lowest first until cleaned', () {
      final c = inventory()
        ..add(logs, 1)
        ..add(logs, 1)
        ..move(1, 7);
      expect(c.dirty, [0, 1, 7]);
      c.markClean();
      expect(c.dirty, isEmpty);
    });

    test('is untouched by a refusal', () {
      final c = inventory()
        ..add(logs, 1)
        ..markClean();
      c.move(0, 0);
      c.swap(5, 6);
      expect(c.dirty, isEmpty);
    });

    test('is untouched when a mirror takes changes in', () {
      final c = inventory();
      c.mirror([(slot: 2, stack: ItemStack(coins, 4))]);
      expect(c.at(2), ItemStack(coins, 4));
      expect(c.dirty, isEmpty);
      c.mirror([(slot: 99, stack: ItemStack(coins, 1))]);
      c.mirror([(slot: 2, stack: null)]);
      expect(c.at(2), isNull);
    });
  });

  group('Container', () {
    test('rejects a size or tab size that does not divide', () {
      final items = testItems();
      expect(() => Container(items: items, size: 0), throwsArgumentError);
      expect(
        () => Container(items: items, size: 10, tabSize: 3),
        throwsArgumentError,
      );
      expect(
        () => Container(items: items, size: 10, tabSize: 0),
        throwsArgumentError,
      );
    });

    test('has the sizes the game promises', () {
      expect(inventory().size, 28);
      expect(bag(ContainerId.equipment).size, equipmentSlots.length);
      expect(bag(ContainerId.bank).size, bankTabs * bankTabSize);
    });
  });

  test('random operations never change the total of any item', () {
    final random = Random(7);
    final items = testItems();
    final inv = inventory();
    final bank = bag(ContainerId.bank);
    final worn = bag(ContainerId.equipment);
    final all = [inv, bank, worn];
    final pool = {coins: maxStack, logs: 200, sword: 3, helmet: 3, arrows: 500};
    final total = {...pool};
    int counted(int item) =>
        pool[item]! + all.fold(0, (sum, c) => sum + c.countOf(item));

    void step() {
      final from = all[random.nextInt(all.length)];
      final to = all[random.nextInt(all.length)];
      final a = random.nextInt(from.size);
      final b = random.nextInt(to.size);
      final item = items.items[random.nextInt(items.items.length)].id;
      final count = 1 + random.nextInt(random.nextBool() ? 5 : maxStack);
      switch (random.nextInt(8)) {
        case 0:
          final wanted = min(count, pool[item]!);
          final put = wanted == 0 ? 0 : from.add(item, wanted);
          pool[item] = pool[item]! - put;
        case 1:
          final taken = from.take(a, count);
          if (taken != null) pool[taken.item] = pool[taken.item]! + taken.count;
        case 2:
          from.move(a, random.nextInt(from.size));
        case 3:
          from.swap(a, random.nextInt(from.size));
        case 4:
          from.stack(a, random.nextInt(from.size));
        case 5:
          from.split(a, random.nextInt(from.size), count);
        case 6:
          from.transfer(a, to, count: count, tab: random.nextBool() ? b : null);
        case 7:
          from.transfer(a, to);
      }
    }

    for (var i = 0; i < 20000; i++) {
      step();
      if (i % 500 == 0) {
        for (final item in total.keys) {
          expect(counted(item), total[item], reason: 'item $item, step $i');
        }
      }
    }
    for (final item in total.keys) {
      expect(counted(item), total[item]);
    }
    expect(
      [
        for (final c in all)
          for (var s = 0; s < c.size; s++) c.at(s),
      ].whereType<ItemStack>().every(
        (s) => items.require(s.item).stackable || s.count == 1,
      ),
      isTrue,
    );
  });

  group('slot deltas', () {
    test('write and read back the slots they name', () {
      final c = inventory()
        ..add(coins, 12)
        ..add(sword, 1);
      final deltas = encodeSlots(ContainerId.inventory, c, [0, 1, 2]);
      expect(deltas, hasLength(1));
      final decoded = tryDecodeSlots(deltas.single, testItems())!;
      expect(decoded.container, ContainerId.inventory);
      expect(decoded.changes, [
        (slot: 0, stack: ItemStack(coins, 12)),
        (slot: 1, stack: ItemStack(sword, 1)),
        (slot: 2, stack: null),
      ]);
    });

    test('split a whole bank across deltas that each fit', () {
      final c = bag(ContainerId.bank);
      for (var i = 0; i < c.size; i++) {
        c.add(logs, 1);
      }
      final deltas = encodeSlots(ContainerId.bank, c, [
        for (var i = 0; i < c.size; i++) i,
      ]);
      expect(deltas.length, greaterThan(1));
      for (final delta in deltas) {
        expect(delta.length, lessThanOrEqualTo(maxDeltaBytes));
      }
      final copy = bag(ContainerId.bank);
      for (final delta in deltas) {
        copy.mirror(tryDecodeSlots(delta, testItems())!.changes);
      }
      expect(
        [for (var i = 0; i < c.size; i++) copy.at(i)],
        [for (var i = 0; i < c.size; i++) c.at(i)],
      );
    });

    test('write nothing for no slots', () {
      expect(encodeSlots(ContainerId.bank, inventory(), const []), isEmpty);
    });

    test('are refused when they are not whole, known or possible', () {
      final items = testItems();
      Uint8List entry(int slot, int item, int count, {int container = 0}) {
        final bytes = Uint8List(10)
          ..[0] = itemsDeltaTag
          ..[1] = container;
        ByteData.sublistView(bytes)
          ..setUint16(2, slot, Endian.little)
          ..setUint16(4, item, Endian.little)
          ..setUint32(6, count, Endian.little);
        return bytes;
      }

      expect(tryDecodeSlots(entry(0, coins, 5), items), isNotNull);
      expect(tryDecodeSlots(entry(0, 0, 0), items), isNotNull);
      expect(tryDecodeSlots(Uint8List(0), items), isNull);
      expect(
        tryDecodeSlots(Uint8List.fromList([itemsDeltaTag]), items),
        isNull,
      );
      expect(tryDecodeSlots(Uint8List.fromList([9, 0]), items), isNull);
      expect(
        tryDecodeSlots(Uint8List.fromList([itemsDeltaTag, 0, 1]), items),
        isNull,
      );
      expect(tryDecodeSlots(entry(0, coins, 5, container: 3), items), isNull);
      expect(tryDecodeSlots(entry(0, 99, 1), items), isNull);
      expect(tryDecodeSlots(entry(0, coins, 0), items), isNull);
      expect(tryDecodeSlots(entry(0, coins, maxStack + 1), items), isNull);
      expect(tryDecodeSlots(entry(0, logs, 2), items), isNull);
      expect(tryDecodeSlots(entry(0, 0, 1), items), isNull);
    });

    test('apply nothing when one entry is bad', () {
      final bytes = Uint8List(18)..[0] = itemsDeltaTag;
      ByteData.sublistView(bytes)
        ..setUint16(2, 0, Endian.little)
        ..setUint16(4, coins, Endian.little)
        ..setUint32(6, 3, Endian.little)
        ..setUint16(10, 1, Endian.little)
        ..setUint16(12, 99, Endian.little)
        ..setUint32(14, 3, Endian.little);
      expect(tryDecodeSlots(bytes, testItems()), isNull);
    });
  });

  group('exchange', () {
    test('takes and gives as one change', () {
      final c = inventory()..add(coins, 50);
      final outcome = c.exchange(
        take: [ItemStack(coins, 30)],
        give: [ItemStack(logs, 2)],
      );
      expect(outcome, Outcome.done);
      expect(c.countOf(coins), 20);
      expect(c.countOf(logs), 2);
    });

    test('takes across stacks', () {
      final c = Container(items: testItems(), size: 4)..add(logs, 3);
      c.exchange(take: [ItemStack(logs, 2)]);
      expect(c.countOf(logs), 1);
    });

    test('changes nothing when the take is not all held', () {
      final c = inventory()..add(coins, 5);
      c.markClean();
      final outcome = c.exchange(
        take: [ItemStack(coins, 6)],
        give: [ItemStack(logs, 1)],
      );
      expect(outcome, Outcome.nothingThere);
      expect(c.countOf(coins), 5);
      expect(c.countOf(logs), 0);
      expect(c.dirty, isEmpty);
    });

    test('changes nothing and marks nothing when the give does not fit', () {
      final c = Container(items: testItems(), size: 2)
        ..add(coins, 10)
        ..add(logs, 1);
      c.markClean();
      final outcome = c.exchange(
        take: [ItemStack(coins, 4)],
        give: [ItemStack(logs, 2)],
      );
      expect(outcome, Outcome.wontFit);
      expect(c.at(0), ItemStack(coins, 10));
      expect(c.at(1), ItemStack(logs, 1));
      expect(c.dirty, isEmpty);
    });

    test('keeps the slots dirty from before a refused exchange', () {
      final c = Container(items: testItems(), size: 1)..add(coins, 10);
      expect(c.dirty, [0]);
      c.exchange(take: [ItemStack(coins, 1)], give: [ItemStack(logs, 2)]);
      expect(c.dirty, [0]);
    });

    test('lets the give use a slot the take freed', () {
      final c = Container(items: testItems(), size: 1)..add(coins, 10);
      final outcome = c.exchange(
        take: [ItemStack(coins, 10)],
        give: [ItemStack(logs, 1)],
      );
      expect(outcome, Outcome.done);
      expect(c.at(0), ItemStack(logs, 1));
    });

    test('throws for an item that does not exist before changing anything', () {
      final c = inventory()..add(coins, 5);
      c.markClean();
      expect(
        () => c.exchange(take: [ItemStack(coins, 1)], give: [ItemStack(99, 1)]),
        throwsArgumentError,
      );
      expect(c.countOf(coins), 5);
      expect(c.dirty, isEmpty);
    });
  });

  group('a view', () {
    Container bank() {
      final c = bag(ContainerId.bank);
      c.add(logs, 2, tab: 0);
      c.add(coins, 9, tab: 3);
      c.add(sword, 1, tab: 3);
      return c;
    }

    test('lists the occupied slots of a tab', () {
      final c = bank();
      expect(c.occupied(tab: 3), [300, 301]);
      expect(c.occupied(tab: 0), [0, 1]);
      expect(c.occupied(tab: 1), isEmpty);
    });

    test('lists every occupied slot with no tab', () {
      expect(bank().occupied(), [0, 1, 300, 301]);
    });

    test('a tab that does not exist is empty', () {
      expect(bank().occupied(tab: bankTabs), isEmpty);
      expect(bank().occupied(tab: -1), isEmpty);
    });

    test('searches names whatever the case', () {
      final c = bank();
      expect(c.search('LOG'), [0, 1]);
      expect(c.search('oin'), [300]);
      expect(c.search('zzz'), isEmpty);
    });

    test('searches inside one tab', () {
      final c = bank();
      expect(c.search('o', tab: 3), [300, 301]);
      expect(c.search('log', tab: 3), isEmpty);
    });

    test('an empty search finds everything held', () {
      expect(bank().search(''), [0, 1, 300, 301]);
    });
  });

  group('restore', () {
    test('puts saved stacks back and marks each slot changed', () {
      final saved = inventory()
        ..add(coins, 40)
        ..add(logs, 2);
      final c = inventory();

      expect(c.restore(saved.slots), 0);
      expect(c.slots, saved.slots);
      expect(c.dirty, [0, 1, 2]);
    });

    test('leaves out an item the game no longer has', () {
      final c = inventory();
      final lost = c.restore([ItemStack(99, 1), ItemStack(coins, 5)]);

      expect(lost, 1);
      expect(c.at(0), isNull);
      expect(c.at(1), ItemStack(coins, 5));
    });

    test('leaves out a stack its slot no longer accepts', () {
      final c = bag(ContainerId.equipment);
      final lost = c.restore([
        ItemStack(sword, 1),
        null,
        null,
        ItemStack(sword, 1),
      ]);

      expect(lost, 1);
      expect(c.at(0), isNull);
      expect(c.at(3), ItemStack(sword, 1));
    });

    test('leaves out a stack past the last slot', () {
      final c = inventory();
      final saved = List<ItemStack?>.filled(inventorySize + 1, null);
      saved[inventorySize] = ItemStack(coins, 1);

      expect(c.restore(saved), 1);
      expect(c.occupied(), isEmpty);
    });

    test('refuses a container that already holds something', () {
      final c = inventory()..add(coins, 1);
      expect(() => c.restore([ItemStack(coins, 1)]), throwsStateError);
    });

    test('slots is a copy that cannot be changed', () {
      final c = inventory()..add(coins, 1);
      final before = c.slots;
      c.add(logs, 1);

      expect(before[1], isNull);
      expect(() => before[0] = null, throwsUnsupportedError);
    });
  });
}
