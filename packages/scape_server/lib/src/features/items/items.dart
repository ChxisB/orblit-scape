import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';

/// The inventory, equipment and bank of every player in the world.
///
/// A player's containers live as long as the player is in the world, and they
/// are lost when the player goes until accounts save them. Whatever changes a
/// container, the owner is sent the slots that changed once a tick.
final class Items {
  Items._(this._hosting, this._items);

  /// Adds containers to [registry] and returns the feature, so gathering,
  /// loot and shops can put items in.
  factory Items.register(
    Registry registry,
    Services services, {
    required ItemSet items,
  }) {
    final feature = Items._(services.hosting, items);
    registry
      ..addIntentHandler(feature._onIntent)
      ..addJoinHandler(feature._join)
      ..addGoneHandler(feature._gone)
      ..addSystem(Phase.outbound, feature._flush);
    return feature;
  }

  final Hosting _hosting;
  final Map<String, Map<ContainerId, Container>> _bags = {};
  final ItemSet _items;

  /// The container of [player], or null for a player who is not in the world.
  Container? container(String player, ContainerId id) => _bags[player]?[id];

  /// A drag swaps two slots. A refusal leaves both as they were and tells
  /// the player, so the panel the client draws never moves on its own.
  void _onIntent(String player, Intent intent) {
    if (intent is! MoveItem) return;
    final from = container(player, intent.from);
    final to = container(player, intent.to);
    if (from == null || to == null) return;
    final outcome = from.swapWith(to, intent.fromSlot, intent.toSlot);
    if (outcome != Outcome.done) {
      _hosting.tell(player, Notice(_refusal(outcome)));
    }
  }

  String _refusal(Outcome outcome) => switch (outcome) {
    Outcome.notAllowed => 'That does not go there.',
    _ => 'There is nothing to move there.',
  };

  void _join(String key) => _bags[key] = {
    for (final id in ContainerId.values) id: Container.forId(id, _items),
  };

  void _gone(String key) => _bags.remove(key);

  void _flush() {
    for (final MapEntry(key: player, value: bags) in _bags.entries) {
      for (final MapEntry(key: id, value: container) in bags.entries) {
        _send(player, id, container);
      }
    }
  }

  void _send(String player, ContainerId id, Container container) {
    for (final delta in encodeSlots(id, container, container.dirty)) {
      _hosting.tell(player, PrivateState(delta));
    }
    container.markClean();
  }
}
