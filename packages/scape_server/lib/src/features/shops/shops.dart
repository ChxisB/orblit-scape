import 'dart:math';

import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../npcs/npcs.dart';
import '../windows/windows.dart';

final class _Stock {
  _Stock(this.line) : count = line.start;

  final ShopLine line;
  int count;

  /// Ticks since the last item came in, while below the cap.
  int waited = 0;

  void restock() {
    if (count >= line.cap) {
      waited = 0;
      return;
    }
    waited++;
    if (waited < line.restockTicks) return;
    count++;
    waited = 0;
  }
}

/// Shopkeepers. Trading with one opens its shop. Stock belongs to the kind of
/// creature, so two keepers of one kind share it. A purchase and a sale each
/// change coins, stock and inventory together or not at all.
final class Shops {
  Shops._(this._services, this._rules, this._items, this._bags, this._windows);

  /// Adds shops to [registry]. Throws [ArgumentError] when a shop stocks an
  /// item that does not exist or the coin itself, or when a full stack of a
  /// line would cost more than a stack of coins can hold.
  static void register(
    Registry registry,
    Services services, {
    required ShopRules rules,
    required ItemSet items,
    required NpcSet npcs,
    required Interactions interactions,
    required Items bags,
    required Windows windows,
  }) {
    items.require(rules.coins);
    final feature = Shops._(services, rules, items, bags, windows);
    for (final npc in npcs.npcs) {
      final shop = npc.shop;
      if (shop != null) feature._stock[npc.id] = feature._stockOf(shop);
    }
    interactions.addNpcAction(NpcOption.trade, feature._open);
    registry
      ..addIntentHandler(feature._onIntent)
      ..addSystem(Phase.outbound, feature._tick);
  }

  final Services _services;
  final ShopRules _rules;
  final ItemSet _items;
  final Items _bags;
  final Windows _windows;

  /// Each shop's lines by creature id, then item.
  final Map<int, Map<int, _Stock>> _stock = {};
  final Set<int> _changed = {};

  Map<int, _Stock> _stockOf(ShopDef shop) {
    for (final line in shop.lines) {
      final price = _rules.buyPrice(_items.require(line.item));
      if (line.item == _rules.coins || price * line.cap > maxStack) {
        throw ArgumentError.value(line.item, 'item', 'cannot be stocked');
      }
    }
    return {for (final line in shop.lines) line.item: _Stock(line)};
  }

  void _open(String player, PlacedNpc npc) {
    if (!_stock.containsKey(npc.def.id)) {
      _tell(player, 'They have nothing to sell.');
      return;
    }
    _windows.open(
      player,
      WindowKind.shop,
      ref: npc.def.id,
      reach: Beside(Footprint.single(npc.tile)),
    );
    _show(player, npc.def.id);
  }

  void _onIntent(String player, Intent intent) {
    final window = _windows.of(player);
    if (window == null || window.kind != WindowKind.shop) return;
    final stock = _stock[window.ref];
    switch (intent) {
      case Buy(:final item, :final count):
        _buy(player, window.ref, stock?[item], count);
      case Sell(:final item, :final count):
        _sell(player, window.ref, stock?[item], count);
      default:
    }
  }

  void _buy(String player, int shop, _Stock? stock, int count) {
    final inventory = _bags.container(player, ContainerId.inventory);
    if (stock == null || inventory == null) {
      _tell(player, 'They do not sell that.');
      return;
    }
    if (stock.count < count) {
      _tell(player, 'They have not got that many.');
      return;
    }
    final item = stock.line.item;
    final price = _rules.buyPrice(_items.require(item)) * count;
    if (inventory.countOf(_rules.coins) < price) {
      _tell(player, 'You cannot afford that.');
      return;
    }
    final outcome = inventory.exchange(
      take: [ItemStack(_rules.coins, price)],
      give: [ItemStack(item, count)],
    );
    if (outcome != Outcome.done) {
      _tell(player, 'You have no room for that.');
      return;
    }
    stock.count -= count;
    _changed.add(shop);
  }

  void _sell(String player, int shop, _Stock? stock, int count) {
    final inventory = _bags.container(player, ContainerId.inventory);
    if (stock == null || inventory == null) {
      _tell(player, 'They do not buy that.');
      return;
    }
    final item = stock.line.item;
    final sold = min(
      count,
      min(stock.line.cap - stock.count, inventory.countOf(item)),
    );
    if (sold < 1) {
      _tell(player, 'They will not take any of that.');
      return;
    }
    final pay = _rules.sellPrice(_items.require(item)) * sold;
    final outcome = inventory.exchange(
      take: [ItemStack(item, sold)],
      give: [ItemStack(_rules.coins, pay)],
    );
    if (outcome != Outcome.done) {
      _tell(player, 'You have no room for the coins.');
      return;
    }
    stock.count += sold;
    _changed.add(shop);
  }

  void _tick() {
    for (final MapEntry(key: shop, value: lines) in _stock.entries) {
      for (final stock in lines.values) {
        final before = stock.count;
        stock.restock();
        if (stock.count != before) _changed.add(shop);
      }
    }
    for (final shop in _changed) {
      for (final player in _windows.viewers(WindowKind.shop, shop)) {
        _show(player, shop);
      }
    }
    _changed.clear();
  }

  void _show(String player, int shop) => _services.hosting.tell(
    player,
    ShopView(
      npc: shop,
      lines: [
        for (final stock in _stock[shop]!.values)
          (item: stock.line.item, count: stock.count),
      ],
    ),
  );

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));
}
