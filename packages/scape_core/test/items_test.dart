import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

void main() {
  group('ItemDef', () {
    test('rejects an id outside 1 to maxItemId', () {
      expect(() => ItemDef(id: 0, name: 'x'), throwsArgumentError);
      expect(() => ItemDef(id: maxItemId + 1, name: 'x'), throwsArgumentError);
      expect(ItemDef(id: maxItemId, name: 'x').id, maxItemId);
    });

    test('rejects an empty name and a negative value', () {
      expect(() => ItemDef(id: 1, name: ''), throwsArgumentError);
      expect(() => ItemDef(id: 1, name: 'x', value: -1), throwsArgumentError);
    });

    test('rejects a slot that is not worn and a level out of range', () {
      expect(
        () => ItemDef(id: 1, name: 'x', equipSlot: 'tail'),
        throwsArgumentError,
      );
      expect(
        () => ItemDef(id: 1, name: 'x', needs: (skill: 'a', level: 0)),
        throwsArgumentError,
      );
      expect(
        () =>
            ItemDef(id: 1, name: 'x', needs: (skill: 'a', level: maxLevel + 1)),
        throwsArgumentError,
      );
    });
  });

  group('ItemSet', () {
    test('finds an item and requires one', () {
      final items = testItems();
      expect(items.find(sword)?.name, 'Sword');
      expect(items.find(99), isNull);
      expect(items.require(coins).stackable, isTrue);
      expect(() => items.require(99), throwsArgumentError);
    });

    test('refuses a repeated id', () {
      expect(
        () => ItemSet(
          skills: testSkills,
          items: [
            ItemDef(id: 1, name: 'a'),
            ItemDef(id: 1, name: 'b'),
          ],
        ),
        throwsArgumentError,
      );
    });

    test('refuses a requirement on a skill the game does not have', () {
      expect(
        () => ItemSet(
          skills: testSkills,
          items: [
            ItemDef(id: 1, name: 'a', needs: (skill: 'baking', level: 2)),
          ],
        ),
        throwsArgumentError,
      );
    });
  });

  group('ItemStack', () {
    test('rejects an item or a count outside range', () {
      expect(() => ItemStack(0, 1), throwsArgumentError);
      expect(() => ItemStack(maxItemId + 1, 1), throwsArgumentError);
      expect(() => ItemStack(1, 0), throwsArgumentError);
      expect(() => ItemStack(1, maxStack + 1), throwsArgumentError);
      expect(ItemStack(1, maxStack).count, maxStack);
    });

    test('compares by value', () {
      expect(ItemStack(1, 2), ItemStack(1, 2));
      expect(ItemStack(1, 2).hashCode, ItemStack(1, 2).hashCode);
      expect(ItemStack(1, 2), isNot(ItemStack(1, 3)));
      expect(ItemStack(1, 2), isNot(ItemStack(2, 2)));
    });
  });
}
