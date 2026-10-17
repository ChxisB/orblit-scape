import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('each creature id names one creature', () {
    for (final id in [
      NpcId.chicken,
      NpcId.goblin,
      NpcId.guard,
      NpcId.shopkeeper,
      NpcId.lamplighter,
    ]) {
      expect(defaultNpcs.find(id), isNotNull);
    }
    expect(defaultNpcs.npcs, hasLength(5));
  });

  test('the creatures behave as their names say', () {
    expect(defaultNpcs.require(NpcId.chicken).behaviour, isA<Wander>());
    expect(defaultNpcs.require(NpcId.guard).behaviour, isA<Idle>());
    expect(defaultNpcs.require(NpcId.goblin).behaviour, isA<Aggressive>());
  });

  test('every item a creature drops or sells is in the item set', () {
    for (final npc in defaultNpcs.npcs) {
      for (final item in npc.loot?.items ?? <int>[]) {
        expect(defaultItems.find(item), isNotNull, reason: npc.name);
      }
      for (final line in npc.shop?.lines ?? <ShopLine>[]) {
        expect(defaultItems.find(line.item), isNotNull, reason: npc.name);
      }
    }
  });

  test('the fighters drop bones, and the shopkeeper does not fight', () {
    for (final id in [NpcId.chicken, NpcId.goblin]) {
      expect(defaultNpcs.require(id).loot!.always, [
        ItemStack(ItemId.bones, 1),
      ]);
    }
    expect(defaultNpcs.require(NpcId.shopkeeper).fight, isNull);
    expect(defaultNpcs.require(NpcId.shopkeeper).shop, isNotNull);
  });

  test('a shop never pays more for an item than it sells it for', () {
    final shop = defaultNpcs.require(NpcId.shopkeeper).shop!;
    for (final line in shop.lines) {
      final item = defaultItems.require(line.item);
      expect(
        defaultShops.sellPrice(item),
        lessThanOrEqualTo(defaultShops.buyPrice(item)),
      );
    }
  });
}
