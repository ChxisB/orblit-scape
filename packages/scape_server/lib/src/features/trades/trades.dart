import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../social/social.dart';
import '../windows/windows.dart';

/// Ticks a request to trade waits for the other player, one minute.
const int _requestTicks = 100;

/// Tiles, each way, two players may stand apart when a trade opens.
const int _openRange = 2;

const String _cancelled = 'The trade was cancelled.';

/// Tells whoever keeps players that the inventories of [a] and [b] changed
/// together and must be saved together.
typedef Settled = void Function(String a, String b);

/// A trade between two players: what each offers, whether it is at the
/// confirm step, and who has accepted the step. An offer is a claim on what
/// the player holds. Nothing moves until the swap.
final class _Session {
  _Session(this.a, this.b) : _offers = {a: {}, b: {}}, _accepted = {};

  final String a;
  final String b;
  final Map<String, Map<int, int>> _offers;
  final Set<String> _accepted;
  bool confirming = false;

  String other(String player) => player == a ? b : a;

  /// How many of [item] [player] offers.
  int offered(String player, int item) => _offers[player]![item] ?? 0;

  /// How many different items [player] offers.
  int linesOf(String player) => _offers[player]!.length;

  bool hasAccepted(String player) => _accepted.contains(player);

  List<ItemStack> stacksOf(String player) => [
    for (final MapEntry(:key, :value) in _offers[player]!.entries)
      ItemStack(key, value),
  ];

  /// Sets what [player] offers of [item]. A count of zero takes it back.
  /// Both accepts go and the trade is back at the offer step.
  void offer(String player, int item, int count) {
    final offers = _offers[player]!;
    if (count == 0) {
      offers.remove(item);
    } else {
      offers[item] = count;
    }
    reset();
  }

  void reset() {
    _accepted.clear();
    confirming = false;
  }

  /// Records that [player] accepted this step, and returns whether that
  /// finishes the confirm step. The first step ends into the confirm step.
  // F5: the caller needs to know when to make the swap.
  bool accept(String player) {
    _accepted.add(player);
    if (_accepted.length < 2) return false;
    _accepted.clear();
    if (confirming) return true;
    confirming = true;
    return false;
  }
}

/// Two players swap items. One clicks the other, the other clicks back, and a
/// window opens for both. Each puts up what they like, both accept, both
/// accept again at the confirm step, and the swap is one change.
///
/// A trade ends when either player leaves, opens another window or walks
/// off the tile they stood on.
final class Trades {
  Trades._(
    this._registry,
    this._services, {
    required Movement movement,
    required Social social,
    required Items bags,
    required ItemSet items,
    required Windows windows,
    required void Function(String line) log,
    required Settled settled,
  }) : _movement = movement,
       _social = social,
       _bags = bags,
       _items = items,
       _windows = windows,
       _log = log,
       _settled = settled;

  /// Adds trading to [registry]. A click on a player asks to trade.
  factory Trades.register(
    Registry registry,
    Services services, {
    required Interactions interactions,
    required Movement movement,
    required Social social,
    required Items bags,
    required ItemSet items,
    required Windows windows,
    required void Function(String line) log,
    required Settled settled,
  }) {
    final feature = Trades._(
      registry,
      services,
      movement: movement,
      social: social,
      bags: bags,
      items: items,
      windows: windows,
      log: log,
      settled: settled,
    );
    interactions.setPlayerAction(feature._request);
    registry
      ..addIntentHandler(feature._onIntent)
      ..addGoneHandler(feature._gone)
      ..addSystem(Phase.outbound, feature._endClosed);
    return feature;
  }

  final Registry _registry;
  final Services _services;
  final Movement _movement;
  final Social _social;
  final Items _bags;
  final ItemSet _items;
  final Windows _windows;
  final void Function(String line) _log;
  final Settled _settled;

  final Map<String, _Session> _sessions = {};
  final Map<String, ({String to, int until})> _requests = {};

  void _request(String player, String other) {
    if (_busy(player)) return _tell(player, 'You are busy.');
    if (_busy(other)) return _tell(player, 'That player is busy.');
    // An ignored sender is told what any sender is told, so ignoring does
    // not give itself away.
    if (_social.isIgnoring(other, player)) {
      return _tell(player, 'Sending a trade request to $other.');
    }
    if (_requests[other]?.to == player) {
      _requests
        ..remove(other)
        ..remove(player);
      return _open(player, other);
    }
    _tell(player, 'Sending a trade request to $other.');
    if (_requests[player]?.to == other) return;
    _requests[player] = (
      to: other,
      until: _registry.actions.tick + _requestTicks,
    );
    _tell(other, '$player wants to trade with you.');
  }

  bool _busy(String player) =>
      _windows.of(player) != null || _sessions.containsKey(player);

  void _open(String player, String other) {
    final here = _movement.standing(player);
    final there = _movement.standing(other);
    if (here == null || there == null || !_near(here.tile, there.tile)) {
      return _tell(player, 'That player is too far away.');
    }
    // Sorted, so the log names the pair the same way whoever clicked last.
    final session = player.compareTo(other) < 0
        ? _Session(player, other)
        : _Session(other, player);
    _sessions[player] = _sessions[other] = session;
    _windows
      ..open(
        player,
        WindowKind.trade,
        ref: there.networkId,
        reach: OnTile(here.tile),
      )
      ..open(
        other,
        WindowKind.trade,
        ref: here.networkId,
        reach: OnTile(there.tile),
      );
    _show(session);
  }

  bool _near(TileCoord a, TileCoord b) =>
      a.plane == b.plane &&
      (a.x - b.x).abs() <= _openRange &&
      (a.y - b.y).abs() <= _openRange;

  void _onIntent(String player, Intent intent) {
    final session = _sessions[player];
    if (session == null) return;
    switch (intent) {
      case TradeOffer(:final item, :final count):
        _offer(session, player, item, count);
      case TradeAccept():
        if (session.accept(player)) {
          _swap(session);
        } else {
          _show(session);
        }
      case TradeDecline():
        _end(session, '$player declined the trade.');
      default:
    }
  }

  void _offer(_Session session, String player, int item, int count) {
    final held = _inventory(player)?.countOf(item) ?? 0;
    if (count > held) return _tell(player, 'You do not have that many.');
    // A repeat of the offer would only set both accepts back.
    if (count == session.offered(player, item)) return;
    if (session.offered(player, item) == 0 &&
        session.linesOf(player) >= maxTradeLines) {
      return _tell(player, 'You cannot offer that many different items.');
    }
    session.offer(player, item, count);
    _show(session);
  }

  /// Makes the swap, or says why it cannot be made and goes back to the
  /// offer step.
  void _swap(_Session session) {
    final players = [session.a, session.b];
    final bags = [for (final player in players) _inventory(player)];
    if (bags.contains(null)) return _end(session, _cancelled);
    final out = [for (final player in players) session.stacksOf(player)];
    final into = out.reversed.toList();
    final blocked = [
      for (final (i, bag) in bags.indexed) _blocker(bag!, out[i], into[i]),
    ];
    if (blocked.any((reason) => reason != null)) {
      return _refuse(session, blocked);
    }
    if (out.every((stacks) => stacks.isEmpty)) {
      return _end(session, 'Nothing was traded.');
    }
    for (final (i, bag) in bags.indexed) {
      bag!.exchange(take: out[i], give: into[i]);
    }
    _settled(session.a, session.b);
    _log(
      'Trade: ${session.a} gave ${_describe(out[0])}, '
      '${session.b} gave ${_describe(out[1])}',
    );
    _end(session, 'The trade is complete.');
  }

  /// What stops [bag] from giving [out] and taking [into], or null.
  String? _blocker(Container bag, List<ItemStack> out, List<ItemStack> into) {
    return switch (bag.tryExchange(take: out, give: into)) {
      Outcome.done => null,
      Outcome.wontFit => 'room for that',
      _ => 'everything offered',
    };
  }

  void _refuse(_Session session, List<String?> reasons) {
    session.reset();
    final players = [session.a, session.b];
    for (final (i, reason) in reasons.indexed) {
      if (reason == null) continue;
      _tell(
        players[i],
        'The trade cannot go through. You do not have $reason.',
      );
      _tell(
        players[1 - i],
        'The trade cannot go through. ${players[i]} does not have $reason.',
      );
    }
    _show(session);
  }

  String _describe(List<ItemStack> stacks) => stacks.isEmpty
      ? 'nothing'
      : stacks
            .map((s) => '${s.count} x ${_items.require(s.item).name}')
            .join(', ');

  void _show(_Session session) {
    for (final player in [session.a, session.b]) {
      final other = session.other(player);
      _services.hosting.tell(
        player,
        TradeView(
          partner: other,
          mine: session.stacksOf(player),
          theirs: session.stacksOf(other),
          confirming: session.confirming,
          youAccepted: session.hasAccepted(player),
          theyAccepted: session.hasAccepted(other),
        ),
      );
    }
  }

  void _end(_Session session, String why) {
    for (final player in [session.a, session.b]) {
      _sessions.remove(player);
      if (_windows.of(player)?.kind == WindowKind.trade) {
        _windows.close(player);
      }
      _tell(player, why);
    }
  }

  void _gone(String player) {
    _requests.remove(player);
    final session = _sessions[player];
    if (session != null) _end(session, '$player left the trade.');
  }

  /// Ends the trade of a player whose window closed, by walking off or by
  /// opening another, and drops requests that nobody answered.
  void _endClosed() {
    final tick = _registry.actions.tick;
    _requests.removeWhere((_, request) => request.until < tick);
    for (final session in _sessions.values.toSet()) {
      final open = [
        session.a,
        session.b,
      ].every((player) => _windows.of(player)?.kind == WindowKind.trade);
      if (!open) _end(session, _cancelled);
    }
  }

  Container? _inventory(String player) =>
      _bags.container(player, ContainerId.inventory);

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));
}
