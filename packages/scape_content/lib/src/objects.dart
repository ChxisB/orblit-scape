import 'package:scape_core/scape_core.dart';

/// The ids of the objects in [defaultObjects].
abstract final class ObjectId {
  static const int door = 1;
  static const int stairsUp = 2;
  static const int stairsDown = 3;
}

/// A door opens and shuts, and stairs lead to the next plane. Where stairs
/// lead is set where each one is placed.
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
  ],
);
