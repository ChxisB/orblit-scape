import 'dart:math' show Random;

import 'items.dart';

/// One way a table can pay out. The chance of picking it is its [weight]
/// over the weights of the whole table.
sealed class LootEntry {
  LootEntry({required this.weight}) {
    if (weight < 1) throw ArgumentError.value(weight, 'weight', 'is below one');
  }

  final int weight;
}

/// Between [min] and [max] of one item.
final class LootItem extends LootEntry {
  LootItem({required super.weight, required this.item, this.min = 1, int? max})
    : max = max ?? min {
    if (item < 1 || item > maxItemId) {
      throw ArgumentError.value(item, 'item', 'is outside 1 to $maxItemId');
    }
    if (min < 1 || this.max < min) {
      throw ArgumentError('A count is at least one and max is not below min.');
    }
  }

  final int item;
  final int min;
  final int max;
}

/// Whatever another table pays out.
final class LootSub extends LootEntry {
  LootSub({required super.weight, required this.table});

  final LootTable table;
}

/// What a kill pays. It always gives [always], gives the [rare] table once in
/// [rareOneIn] rolls, and picks one of [entries]. [nothing] is the weight of
/// picking none of them. The dice are the caller's, so a test can fix them.
final class LootTable {
  LootTable({
    List<ItemStack> always = const [],
    List<LootEntry> entries = const [],
    this.nothing = 0,
    this.rare,
    this.rareOneIn = 0,
  }) : always = List.unmodifiable(always),
       entries = List.unmodifiable(entries),
       _total = nothing + entries.fold(0, (sum, entry) => sum + entry.weight) {
    if (nothing < 0) {
      throw ArgumentError.value(nothing, 'nothing', 'is negative');
    }
    if (rare == null ? rareOneIn != 0 : rareOneIn < 2) {
      throw ArgumentError.value(
        rareOneIn,
        'rareOneIn',
        'must be 2 or more with a rare table, and zero without one',
      );
    }
  }

  final List<ItemStack> always;
  final List<LootEntry> entries;
  final int nothing;
  final LootTable? rare;
  final int rareOneIn;
  final int _total;

  /// Every item the table can pay out, so content can check each exists.
  Iterable<int> get items => [
    for (final stack in always) stack.item,
    for (final entry in entries)
      ...switch (entry) {
        LootItem(:final item) => [item],
        LootSub(:final table) => table.items,
      },
    ...?rare?.items,
  ];

  /// One roll of the table.
  // F5: a roll answers with what it paid and spends the dice it is given.
  List<ItemStack> roll(Random random) {
    final paid = [...always];
    final rare = this.rare;
    if (rare != null && random.nextInt(rareOneIn) == 0) {
      paid.addAll(rare.roll(random));
    }
    switch (_pick(random)) {
      case LootItem(:final item, :final min, :final max):
        paid.add(ItemStack(item, min + random.nextInt(max - min + 1)));
      case LootSub(:final table):
        paid.addAll(table.roll(random));
      case null:
    }
    return paid;
  }

  LootEntry? _pick(Random random) {
    if (_total == 0) return null;
    var at = random.nextInt(_total);
    for (final entry in entries) {
      if (at < entry.weight) return entry;
      at -= entry.weight;
    }
    return null;
  }
}
