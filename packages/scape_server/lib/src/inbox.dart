import 'package:scape_core/scape_core.dart';

/// What a player asked for, and who asked.
typedef PlayerIntent = ({String player, Intent intent});

/// Intents that have arrived and not yet been turned into actions.
///
/// The inbound phase drains it once a tick. A player keeps at most
/// [perPlayer] waiting, so one that sends faster than the tick runs cannot
/// grow it. The first ones stay, because they came first.
final class Inbox {
  Inbox({this.perPlayer = 20}) {
    if (perPlayer < 1) {
      throw ArgumentError.value(perPlayer, 'perPlayer', 'Must be 1 or more');
    }
  }

  final int perPlayer;
  final List<PlayerIntent> _waiting = [];
  final Map<String, int> _count = {};

  /// Intents dropped because their player already had [perPlayer] waiting.
  int dropped = 0;

  int pending(String player) => _count[player] ?? 0;

  void add(String player, Intent intent) {
    if (pending(player) >= perPlayer) {
      dropped++;
      return;
    }
    _count[player] = pending(player) + 1;
    _waiting.add((player: player, intent: intent));
  }

  /// Everything waiting, oldest first. The inbox is empty afterwards.
  List<PlayerIntent> drain() {
    final all = List.of(_waiting);
    _waiting.clear();
    _count.clear();
    return all;
  }

  void forget(String player) {
    _waiting.removeWhere((entry) => entry.player == player);
    _count.remove(player);
  }
}
