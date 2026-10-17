import 'skills.dart';

/// The most one stack holds. It fits a signed 32-bit integer, so a stack adds
/// the same on every platform.
const int maxStack = 0x7FFFFFFF;

/// The highest item id. Zero means an empty slot on the wire.
const int maxItemId = 0xFFFF;

/// The first byte of a [PrivateState] delta that carries container slots.
const int itemsDeltaTag = 2;

/// The name of the replicated component on an item lying on the ground.
const String groundItemName = 'GroundItem';

/// Ints in that component: the item id, then the count.
const int groundItemInts = 2;

/// The places a worn item may go, in the order the equipment container holds
/// them.
const List<String> equipmentSlots = [
  'head',
  'neck',
  'cape',
  'weapon',
  'body',
  'shield',
  'legs',
  'hands',
  'feet',
  'ring',
];

/// The skill level a player needs to use an item.
typedef Needs = ({String skill, int level});

/// One kind of item: a row of content.
final class ItemDef {
  ItemDef({
    required this.id,
    required this.name,
    this.stackable = false,
    this.value = 0,
    this.equipSlot,
    this.needs,
  }) {
    if (id < 1 || id > maxItemId) {
      throw ArgumentError.value(id, 'id', 'is outside 1 to $maxItemId');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (value < 0) throw ArgumentError.value(value, 'value', 'is negative');
    if (equipSlot != null && !equipmentSlots.contains(equipSlot)) {
      throw ArgumentError.value(equipSlot, 'equipSlot', 'is not a slot');
    }
    if (needs != null && (needs!.level < 1 || needs!.level > maxLevel)) {
      throw ArgumentError.value(needs, 'needs', 'has a level outside range');
    }
  }

  final int id;
  final String name;

  /// Whether a count of this item shares one slot.
  final bool stackable;

  /// What a shop pays, in coins.
  final int value;

  /// The slot it is worn in, or null for an item that is not worn.
  final String? equipSlot;

  final Needs? needs;
}

/// The items a game has. A skill an item names must exist, so a typo fails
/// when the game starts and not when a player tries the item.
final class ItemSet {
  ItemSet({required List<ItemDef> items, required SkillSet skills})
    : items = List.unmodifiable(items),
      _byId = {for (final item in items) item.id: item} {
    if (_byId.length != items.length) {
      throw ArgumentError.value(items, 'items', 'repeats an id');
    }
    for (final item in items) {
      final needs = item.needs;
      if (needs != null && skills.find(needs.skill) == null) {
        throw ArgumentError.value(item.id, 'items', 'needs an unknown skill');
      }
    }
  }

  final List<ItemDef> items;
  final Map<int, ItemDef> _byId;

  ItemDef? find(int id) => _byId[id];

  /// The item called [id]. Throws [ArgumentError] when there is none, which
  /// is how content that names a missing item fails at startup.
  ItemDef require(int id) =>
      _byId[id] ?? (throw ArgumentError.value(id, 'id', 'is not an item'));
}

/// A count of one kind of item in a slot.
final class ItemStack {
  ItemStack(this.item, this.count) {
    if (item < 1 || item > maxItemId) {
      throw ArgumentError.value(item, 'item', 'is outside 1 to $maxItemId');
    }
    if (count < 1 || count > maxStack) {
      throw ArgumentError.value(count, 'count', 'is outside 1 to $maxStack');
    }
  }

  final int item;
  final int count;

  @override
  bool operator ==(Object other) =>
      other is ItemStack && other.item == item && other.count == count;

  @override
  int get hashCode => Object.hash(item, count);

  @override
  String toString() => 'ItemStack($item x $count)';
}
