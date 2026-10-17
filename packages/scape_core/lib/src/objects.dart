import 'reach.dart' show Footprint;
import 'tile_coord.dart';

/// The name of the replicated component on an object standing in the world.
const String worldObjectName = 'WorldObject';

/// Ints in that component: the object id, then its state.
const int worldObjectInts = 2;

/// The highest object id.
const int maxObjectId = 0xFFFF;

/// What the first byte of an [Interact] names. The id and option that follow
/// mean what each kind says.
abstract final class InteractKind {
  /// A placed object. The id is its network id, the option an index into its
  /// options.
  static const int object = 0;

  /// An item lying on the ground. The id is its network id. The option is
  /// unused.
  static const int ground = 1;

  /// An inventory item used on an object. The id is the object's network id,
  /// the option the inventory slot.
  static const int useOnObject = 2;

  /// An inventory item used on another. The id is the slot of the item
  /// used on, the option the slot of the item used.
  static const int useOnItem = 3;

  /// A creature. The id is its network id. The option is unused, and the
  /// click is an attack.
  static const int npc = 4;

  /// Another player. The id is their network id. The option is unused, and
  /// the click asks to trade.
  static const int player = 5;
}

/// One entry in the menu of an object. [action] names what a feature runs.
/// With [whenState] set the option is offered only while the object is in
/// that state, so a door lists Open when shut and Close when open.
final class ObjectOption {
  ObjectOption({required this.label, required this.action, this.whenState}) {
    if (label.isEmpty) throw ArgumentError.value(label, 'label', 'is empty');
    if (action.isEmpty) throw ArgumentError.value(action, 'action', 'is empty');
  }

  final String label;
  final String action;
  final int? whenState;

  bool offeredIn(int state) => whenState == null || whenState == state;
}

/// One kind of object: a row of content.
///
/// State 0 is the whole object. Any other state is a changed one, such as an
/// open door or a felled tree. A [solid] object blocks its tiles in state 0
/// only.
final class ObjectDef {
  ObjectDef({
    required this.id,
    required this.name,
    this.width = 1,
    this.height = 1,
    this.solid = false,
    List<ObjectOption> options = const [],
  }) : options = List.unmodifiable(options) {
    if (id < 1 || id > maxObjectId) {
      throw ArgumentError.value(id, 'id', 'is outside 1 to $maxObjectId');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (width < 1 || height < 1) {
      throw ArgumentError('An object is at least one tile each way.');
    }
    if (options.length > 0xFF) {
      throw ArgumentError.value(options.length, 'options', 'is over 255');
    }
  }

  final int id;
  final String name;
  final int width;
  final int height;
  final bool solid;
  final List<ObjectOption> options;

  /// Whether the object blocks the tiles it stands on while in [state].
  bool blocksIn(int state) => solid && state == 0;

  /// The tiles this object covers when its south-west tile is [origin].
  Footprint footprintAt(TileCoord origin) => Footprint(origin, width, height);
}

/// The objects a game has.
final class ObjectSet {
  ObjectSet({required List<ObjectDef> objects})
    : objects = List.unmodifiable(objects),
      _byId = {for (final object in objects) object.id: object} {
    if (_byId.length != objects.length) {
      throw ArgumentError.value(objects, 'objects', 'repeats an id');
    }
  }

  final List<ObjectDef> objects;
  final Map<int, ObjectDef> _byId;

  ObjectDef? find(int id) => _byId[id];

  /// The object called [id]. Throws [ArgumentError] when there is none.
  ObjectDef require(int id) =>
      _byId[id] ?? (throw ArgumentError.value(id, 'id', 'is not an object'));
}
