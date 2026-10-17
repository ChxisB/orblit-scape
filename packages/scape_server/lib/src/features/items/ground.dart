import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';

/// Ticks an item belongs to the player who dropped or earned it, 60 seconds.
const int groundOwnerTicks = 100;

/// Ticks it then lies where anyone may take it before it vanishes.
const int groundPublicTicks = 100;

/// The tile [player] stands on, or null when they are not in the world.
typedef TileOf = TileCoord? Function(String player);

/// The inventory of [player], or null when they are not in the world.
typedef InventoryOf = Container? Function(String player);

/// An item lying on the ground, as a query answers it.
typedef Lying = ({int networkId, int item, int count});

final class _Item {
  _Item({
    required this.entity,
    required this.networkId,
    required this.item,
    required this.count,
    required this.tile,
    required this.owner,
    required this.ownedUntil,
    required this.vanishAt,
  });

  final int entity;
  final int networkId;
  final int item;
  final TileCoord tile;
  final int ownedUntil;
  final int vanishAt;
  int count;
  String? owner;
}

/// Items lying on tiles. Each is an entity the clients near it draw, with a
/// timer: first it belongs to one player and the others are not told about
/// it, then it belongs to everyone, then it vanishes.
final class GroundItems {
  GroundItems._({
    required Registry registry,
    required Services services,
    required ItemSet items,
    required TileOf tileOf,
    required InventoryOf inventoryOf,
    required ComponentType position,
    required ComponentType ground,
  }) : _registry = registry,
       _services = services,
       _items = items,
       _tileOf = tileOf,
       _inventoryOf = inventoryOf,
       _position = position,
       _ground = ground;

  /// Adds ground items to [registry] and returns the feature, so loot and
  /// gathering can put items down and an interaction can pick them up.
  factory GroundItems.register(
    Registry registry,
    Services services, {
    required ItemSet items,
    required TileOf tileOf,
    required InventoryOf inventoryOf,
  }) {
    final world = services.world;
    final ground = world.registerComponent(
      groundItemName,
      kind: ComponentKind.int32,
      arity: groundItemInts,
    );
    final feature = GroundItems._(
      registry: registry,
      services: services,
      items: items,
      tileOf: tileOf,
      inventoryOf: inventoryOf,
      position: world.registerComponent(
        tilePositionName,
        kind: ComponentKind.float32,
        arity: tilePositionFloats,
      ),
      ground: ground,
    );
    registry
      ..replicate(ground)
      ..addSystem(Phase.outbound, feature._expire);
    return feature;
  }

  final Registry _registry;
  final Services _services;
  final ItemSet _items;
  final TileOf _tileOf;
  final InventoryOf _inventoryOf;
  final ComponentType _position;
  final ComponentType _ground;
  final Map<int, _Item> _lying = {};

  /// Whether [player] may be told about [networkId]. Anything that is not an
  /// item on the ground, and any item no longer owned, is open to all.
  bool canSee(String player, int networkId) {
    final owner = _lying[networkId]?.owner;
    return owner == null || owner == player;
  }

  /// What [player] can see on [tile].
  List<Lying> lyingAt(String player, TileCoord tile) => [
    for (final item in _lying.values)
      if (item.tile == tile && canSee(player, item.networkId))
        (networkId: item.networkId, item: item.item, count: item.count),
  ];

  /// Puts [count] of [item] on [at]. With an [owner] only that player is told
  /// of it until the owner timer ends. Returns the entity's network id.
  /// Throws [ArgumentError] for an item that does not exist or a count
  /// outside range.
  // F5: the network id is how a caller finds the item it just put down.
  int drop(int item, int count, {required TileCoord at, String? owner}) {
    final stack = ItemStack(_items.require(item).id, count);
    final now = _registry.actions.tick;
    final ownedUntil = owner == null ? now : now + groundOwnerTicks;
    final world = _services.world;
    final entity = world.createEntity();
    world
      ..add(entity, _position)
      ..add(entity, _ground);
    writeTilePosition(world.bytesOf(entity, _position)!, at);
    ByteData.sublistView(world.bytesOf(entity, _ground)!)
      ..setInt32(0, stack.item, Endian.little)
      ..setInt32(4, stack.count, Endian.little);
    final networkId = _services.hosting.spawn(entity);
    _lying[networkId] = _Item(
      entity: entity,
      networkId: networkId,
      item: stack.item,
      count: stack.count,
      tile: at,
      owner: owner,
      ownedUntil: ownedUntil,
      vanishAt: ownedUntil + groundPublicTicks,
    );
    return networkId;
  }

  /// Drops [count] of the stack in [slot] of [player]'s inventory on the tile
  /// they stand on, or the whole stack. It belongs to them for a while.
  /// [Outcome.nothingThere] is anything that stopped it.
  // F5: the outcome is the caller's cue to tell the player.
  Outcome dropFrom(String player, int slot, {int? count}) {
    final tile = _tileOf(player);
    if (tile == null) return Outcome.nothingThere;
    final taken = _inventoryOf(player)?.take(slot, count);
    if (taken == null) return Outcome.nothingThere;
    drop(taken.item, taken.count, at: tile, owner: player);
    return Outcome.done;
  }

  /// Moves the item [networkId] into [player]'s inventory. The player must
  /// stand on its tile and be allowed to see it. A bag with room for only
  /// part takes that part and leaves the rest lying there.
  // F5: the outcome is the caller's cue to tell the player.
  Outcome pickUp(String player, int networkId) {
    final item = _lying[networkId];
    final inventory = _inventoryOf(player);
    if (item == null || inventory == null || !canSee(player, networkId)) {
      return Outcome.nothingThere;
    }
    if (_tileOf(player) != item.tile) return Outcome.notAllowed;
    final added = inventory.add(item.item, item.count);
    if (added == 0) return Outcome.wontFit;
    if (added == item.count) {
      _remove(item);
      return Outcome.done;
    }
    item.count -= added;
    ByteData.sublistView(
      _services.world.bytesOf(item.entity, _ground)!,
    ).setInt32(4, item.count, Endian.little);
    return Outcome.partly;
  }

  void _expire() {
    final now = _registry.actions.tick;
    for (final item in _lying.values.toList()) {
      if (now >= item.vanishAt) {
        _remove(item);
      } else if (now >= item.ownedUntil) {
        item.owner = null;
      }
    }
  }

  void _remove(_Item item) {
    _lying.remove(item.networkId);
    _services.hosting.despawn(item.entity);
  }
}
