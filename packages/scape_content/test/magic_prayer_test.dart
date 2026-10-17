import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('the bow is ranged and the arrows fit the ammo slot', () {
    final bow = defaultItems.require(ItemId.shortbow).combat!;
    final arrows = defaultItems.require(ItemId.bronzeArrows);

    expect(bow.style, CombatStyle.ranged);
    expect(arrows.combat!.ammo, bow.ammo);
    expect(arrows.equipSlot, 'ammo');
    expect(arrows.stackable, isTrue);
  });

  test('every spell and prayer is found by its id', () {
    expect(defaultSpells.find(SpellId.strike)!.effect, isA<SpellDamage>());
    expect(defaultSpells.find(SpellId.daze)!.effect, isA<SpellStun>());
    expect(defaultSpells.find(SpellId.home)!.effect, isA<SpellTeleport>());
    expect(defaultPrayers.find(PrayerId.thickSkin)!.stat, CombatStat.defence);
    expect(defaultPrayers.prayers, hasLength(3));
  });

  test('a prayer that drains five a tick spends a point in twenty ticks', () {
    final drain = defaultPrayers.find(PrayerId.thickSkin)!.drain;

    expect(defaultPrayers.drainPerPoint ~/ drain, 20);
  });
}
