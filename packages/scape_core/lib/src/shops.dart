import 'dart:math' show max;

import 'items.dart';

/// The most lines one shop may stock. A shop's whole stock is sent in one
/// message.
const int maxShopLines = 40;

/// The most one line may hold.
const int maxShopStock = 1000000;

/// How a game prices things. A shop sells an item for its value times
/// [buyPercent] and pays for one at its value times [sellPercent], and a price
/// is never below one coin. Buying back never pays more than the sale.
final class ShopRules {
  ShopRules({
    required this.coins,
    this.buyPercent = 130,
    this.sellPercent = 40,
  }) {
    if (coins < 1 || coins > maxItemId) {
      throw ArgumentError.value(coins, 'coins', 'is outside 1 to $maxItemId');
    }
    if (sellPercent < 1 || buyPercent < sellPercent) {
      throw ArgumentError('A shop sells for at least what it pays.');
    }
  }

  /// The item a price is paid in.
  final int coins;
  final int buyPercent;
  final int sellPercent;

  /// What a player pays for one [item].
  int buyPrice(ItemDef item) => max(1, item.value * buyPercent ~/ 100);

  /// What a player is paid for one [item].
  int sellPrice(ItemDef item) => max(1, item.value * sellPercent ~/ 100);
}

/// One item a shop holds: it starts at [start], which is [cap] unless given,
/// and gains one every [restockTicks] ticks while below [cap]. It buys that
/// item only up to [cap], so a line that starts low is one players supply.
final class ShopLine {
  ShopLine({
    required this.item,
    required this.cap,
    int? start,
    this.restockTicks = 50,
  }) : start = start ?? cap {
    if (item < 1 || item > maxItemId) {
      throw ArgumentError.value(item, 'item', 'is outside 1 to $maxItemId');
    }
    if (cap < 1 || cap > maxShopStock) {
      throw ArgumentError.value(cap, 'cap', 'is outside 1 to $maxShopStock');
    }
    if (this.start < 0 || this.start > cap) {
      throw ArgumentError.value(start, 'start', 'is outside 0 to $cap');
    }
    if (restockTicks < 1) {
      throw ArgumentError.value(restockTicks, 'restockTicks', 'is below one');
    }
  }

  final int item;
  final int cap;
  final int start;
  final int restockTicks;
}

/// What a shopkeeper stocks.
final class ShopDef {
  ShopDef({required List<ShopLine> lines}) : lines = List.unmodifiable(lines) {
    if (lines.isEmpty || lines.length > maxShopLines) {
      throw ArgumentError.value(
        lines.length,
        'lines',
        'is outside 1 to $maxShopLines',
      );
    }
    if ({for (final line in lines) line.item}.length != lines.length) {
      throw ArgumentError.value(lines, 'lines', 'repeats an item');
    }
  }

  final List<ShopLine> lines;

  ShopLine? find(int item) {
    for (final line in lines) {
      if (line.item == item) return line;
    }
    return null;
  }
}
