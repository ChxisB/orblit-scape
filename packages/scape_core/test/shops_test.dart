import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

void main() {
  final items = testItems();
  final rules = ShopRules(coins: coins);

  group('prices', () {
    test('a shop sells above the value and pays below it', () {
      final item = items.require(logs);
      expect(rules.buyPrice(item), 5);
      expect(rules.sellPrice(item), 1);
    });

    test('a worthless item still costs a coin', () {
      final item = items.require(helmet);
      expect(rules.buyPrice(item), 1);
      expect(rules.sellPrice(item), 1);
    });

    test('a sale never pays more than the purchase cost', () {
      for (final item in items.items) {
        expect(rules.sellPrice(item), lessThanOrEqualTo(rules.buyPrice(item)));
      }
    });

    test('refuses to pay more than it sells for', () {
      expect(
        () => ShopRules(coins: coins, buyPercent: 50, sellPercent: 60),
        throwsArgumentError,
      );
      expect(() => ShopRules(coins: 0), throwsArgumentError);
      expect(
        () => ShopRules(coins: coins, sellPercent: 0),
        throwsArgumentError,
      );
    });
  });

  group('a shop', () {
    test('finds a line by item', () {
      final shop = ShopDef(lines: [ShopLine(item: logs, cap: 10)]);
      expect(shop.find(logs)?.cap, 10);
      expect(shop.find(sword), isNull);
    });

    test('refuses no lines, too many and a repeated item', () {
      expect(() => ShopDef(lines: []), throwsArgumentError);
      expect(
        () => ShopDef(
          lines: [
            ShopLine(item: logs, cap: 1),
            ShopLine(item: logs, cap: 2),
          ],
        ),
        throwsArgumentError,
      );
      expect(
        () => ShopDef(
          lines: [
            for (var i = 1; i <= maxShopLines + 1; i++)
              ShopLine(item: i, cap: 1),
          ],
        ),
        throwsArgumentError,
      );
    });

    test('a line starts full unless it says otherwise', () {
      expect(ShopLine(item: logs, cap: 8).start, 8);
      expect(ShopLine(item: logs, cap: 8, start: 0).start, 0);
    });

    test('refuses a line with no room, no cap, no restock or a bad start', () {
      expect(() => ShopLine(item: 0, cap: 1), throwsArgumentError);
      expect(() => ShopLine(item: logs, cap: 0), throwsArgumentError);
      expect(
        () => ShopLine(item: logs, cap: maxShopStock + 1),
        throwsArgumentError,
      );
      expect(
        () => ShopLine(item: logs, cap: 1, restockTicks: 0),
        throwsArgumentError,
      );
      expect(() => ShopLine(item: logs, cap: 2, start: 3), throwsArgumentError);
      expect(
        () => ShopLine(item: logs, cap: 2, start: -1),
        throwsArgumentError,
      );
    });
  });
}
