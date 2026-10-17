import 'package:scape_core/scape_core.dart';

/// The ids of the creatures in [defaultNpcs].
abstract final class NpcId {
  static const int chicken = 1;
  static const int goblin = 2;
  static const int guard = 3;
}

/// A chicken wanders, a goblin goes for anyone who comes near and a guard
/// stands where it is put.
final NpcSet defaultNpcs = NpcSet(
  npcs: [
    NpcDef(id: NpcId.chicken, name: 'Chicken', behaviour: const Wander()),
    NpcDef(
      id: NpcId.goblin,
      name: 'Goblin',
      behaviour: Aggressive(sight: 5, leash: 12),
    ),
    NpcDef(id: NpcId.guard, name: 'Guard'),
  ],
);
