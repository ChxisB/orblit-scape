import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('every named item is in the set under its own id', () {
    const named = {
      ItemId.coins: 'Coins',
      ItemId.logs: 'Logs',
      ItemId.copperOre: 'Copper ore',
      ItemId.bronzeBar: 'Bronze bar',
      ItemId.rawFish: 'Raw fish',
      ItemId.cookedFish: 'Cooked fish',
      ItemId.bronzeAxe: 'Bronze axe',
      ItemId.bronzePickaxe: 'Bronze pickaxe',
      ItemId.bronzeSword: 'Bronze sword',
      ItemId.woodenShield: 'Wooden shield',
      ItemId.bronzeHelmet: 'Bronze helmet',
    };

    expect(defaultItems.items, hasLength(named.length));
    for (final MapEntry(key: id, value: name) in named.entries) {
      expect(defaultItems.require(id).name, name);
    }
  });

  test('only coins stack', () {
    final stackable = [
      for (final item in defaultItems.items)
        if (item.stackable) item.id,
    ];

    expect(stackable, [ItemId.coins]);
  });

  test('worn items name a slot and tools need a level', () {
    expect(defaultItems.require(ItemId.bronzeHelmet).equipSlot, 'head');
    expect(defaultItems.require(ItemId.woodenShield).equipSlot, 'shield');
    expect(defaultItems.require(ItemId.bronzeSword).equipSlot, 'weapon');
    expect(defaultItems.require(ItemId.bronzeAxe).needs, isNotNull);
    expect(defaultItems.require(ItemId.logs).equipSlot, isNull);
  });

  test('an equipment container takes the helmet and refuses the logs', () {
    final worn = Container.forId(ContainerId.equipment, defaultItems);

    expect(worn.add(ItemId.bronzeHelmet, 1), 1);
    expect(worn.add(ItemId.logs, 1), 0);
  });
}
