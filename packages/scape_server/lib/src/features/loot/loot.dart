import 'dart:math';

import 'package:scape_core/scape_core.dart';

import '../combat/combat.dart';
import '../items/ground.dart';

/// Puts what a creature's loot table pays on the ground where it died. The
/// killer sees it first. Throws [ArgumentError] when a table names an item
/// that does not exist, so a typo fails at startup and not at the first kill.
void registerLoot({
  required Combat combat,
  required GroundItems ground,
  required ItemSet items,
  required NpcSet npcs,
  required Random random,
}) {
  for (final npc in npcs.npcs) {
    npc.loot?.items.forEach(items.require);
  }
  combat.addKillHandler((killer, npc) {
    final table = npc.def.loot;
    if (table == null) return;
    for (final stack in table.roll(random)) {
      ground.drop(stack.item, stack.count, at: npc.tile, owner: killer);
    }
  });
}
