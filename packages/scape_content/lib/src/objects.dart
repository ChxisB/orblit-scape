import 'package:scape_core/scape_core.dart';

/// The ids of the objects in [defaultObjects].
abstract final class ObjectId {
  static const int door = 1;
  static const int stairsUp = 2;
  static const int stairsDown = 3;
  static const int tree = 4;
  static const int rock = 5;
  static const int fishingSpot = 6;
  static const int bankBooth = 7;
  static const int fire = 8;
  static const int anvil = 9;
}

/// A door opens and shuts, and stairs lead to the next plane. Where stairs
/// lead is set where each one is placed. A tree, a rock and a fishing spot
/// can be gathered from while in state 0 and are bare in state 1. A bank
/// booth opens the player's bank. A fire and an anvil offer nothing to pick.
/// Recipes use them as stations.
final ObjectSet defaultObjects = ObjectSet(
  objects: [
    ObjectDef(
      id: ObjectId.door,
      name: 'Door',
      solid: true,
      options: [
        ObjectOption(label: 'Open', action: 'toggle', whenState: 0),
        ObjectOption(label: 'Close', action: 'toggle', whenState: 1),
      ],
    ),
    ObjectDef(
      id: ObjectId.stairsUp,
      name: 'Stairs',
      options: [ObjectOption(label: 'Climb up', action: 'climb')],
    ),
    ObjectDef(
      id: ObjectId.stairsDown,
      name: 'Stairs',
      options: [ObjectOption(label: 'Climb down', action: 'climb')],
    ),
    ObjectDef(
      id: ObjectId.tree,
      name: 'Tree',
      solid: true,
      options: [
        ObjectOption(label: 'Chop down', action: 'gather', whenState: 0),
      ],
    ),
    ObjectDef(
      id: ObjectId.rock,
      name: 'Rock',
      solid: true,
      options: [ObjectOption(label: 'Mine', action: 'gather', whenState: 0)],
    ),
    ObjectDef(
      id: ObjectId.fishingSpot,
      name: 'Fishing spot',
      options: [ObjectOption(label: 'Net', action: 'gather', whenState: 0)],
    ),
    ObjectDef(
      id: ObjectId.bankBooth,
      name: 'Bank booth',
      solid: true,
      options: [ObjectOption(label: 'Bank', action: 'bank')],
    ),
    ObjectDef(id: ObjectId.fire, name: 'Fire'),
    ObjectDef(id: ObjectId.anvil, name: 'Anvil', solid: true),
  ],
);
