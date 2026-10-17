import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../movement/movement.dart';

/// What a window shows: a shop, a bank, a conversation or a trade.
enum WindowKind { shop, bank, talk, trade }

/// The window a player has open: what it is, which of its kind ([ref] is the
/// creature id of the shopkeeper or of whoever they talk to, the network id
/// of the trade partner, and zero for a bank), and where the player has to
/// stand to keep using it.
typedef OpenWindow = ({WindowKind kind, int ref, Reach reach});

/// The window each player has open. A player has one at a time.
/// It closes when they stop being able to reach what it belongs to, however
/// that came about, and the client is told.
final class Windows {
  Windows._(this._services, this._movement);

  /// Adds windows to [registry] and returns the feature, so shops and banks
  /// can open one and check a request against it.
  factory Windows.register(
    Registry registry,
    Services services, {
    required Movement movement,
  }) {
    final feature = Windows._(services, movement);
    registry
      ..addGoneHandler(feature._open.remove)
      ..addSystem(Phase.outbound, feature._closeOutOfReach);
    return feature;
  }

  final Services _services;
  final Movement _movement;
  final Map<String, OpenWindow> _open = {};

  /// The window [player] has open, or null.
  OpenWindow? of(String player) => _open[player];

  /// The players who have the [kind] window called [ref] open.
  List<String> viewers(WindowKind kind, int ref) => [
    for (final MapEntry(key: player, value: window) in _open.entries)
      if (window.kind == kind && window.ref == ref) player,
  ];

  /// Opens a window for [player] in place of the one they had. The reach is
  /// fixed now, so a shopkeeper who wanders off does not close it.
  void open(
    String player,
    WindowKind kind, {
    required int ref,
    required Reach reach,
  }) => _open[player] = (kind: kind, ref: ref, reach: reach);

  void close(String player) {
    if (_open.remove(player) != null) {
      _services.hosting.tell(player, const WindowClosed());
    }
  }

  void _closeOutOfReach() {
    for (final MapEntry(key: player, value: window) in _open.entries.toList()) {
      final tile = _movement.tileOf(player);
      if (tile == null || !reachSatisfied(_services.map, window.reach, tile)) {
        close(player);
      }
    }
  }
}
