import 'package:flutter/foundation.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';

import 'panel_state.dart';

typedef _Handler = void Function(List<String> args);

/// Turns what a panel's buttons say into messages and into changes of
/// [PanelState]. It never changes what the server decides: a drag is sent and
/// the slots change when the server's answer arrives.
final class PanelController extends ChangeNotifier {
  PanelController({required this.session, required this.menus}) {
    _handlers = {
      'tab': (a) => _set(state.copyWith(tab: SideTab.values.byName(a[0]))),
      'chat': (a) => _set(state.copyWith(chat: ChatTab.values.byName(a[0]))),
      'slot': (a) => _tapSlot(ContainerId.values.byName(a[0]), int.parse(a[1])),
      'menu': (a) => _runMenu(int.parse(a[0])),
      'menu-close': (_) => _set(state.copyWith(menu: const [])),
      'volume': (a) =>
          _set(state.copyWith(volume: _step(state.volume, a[0], 0, maxVolume))),
      'view': (a) => _set(
        state.copyWith(
          viewDistance: _step(
            state.viewDistance,
            a[0],
            minViewDistance,
            maxViewDistance,
          ),
        ),
      ),
      'continue': (_) => session.continueTalk(),
      'choose': (a) => session.choose(int.parse(a[0])),
      'buy': (a) => session.buy(int.parse(a[0]), 1),
      'withdraw': (a) => session.withdraw(int.parse(a[0]), 1),
      'accept': (_) => session.acceptTrade(),
      'decline': (_) => session.declineTrade(),
    };
  }

  final GameSession session;
  final Menus menus;
  PanelState state = PanelState();
  late final Map<String, _Handler> _handlers;

  /// Tells the panels to redraw. Call it once a frame, after
  /// [GameSession.advance], because the session has no listener of its own.
  void refresh() => notifyListeners();

  /// Runs what a button said, such as `slot:inventory:3`.
  void handle(String handler) {
    final parts = handler.split(':');
    _handlers[parts.first]?.call(parts.sublist(1));
    notifyListeners();
  }

  /// Shows [entries] as the right-click menu, or hides it when empty.
  void openMenu(List<MenuEntry> entries) {
    _set(state.copyWith(menu: entries));
    notifyListeners();
  }

  /// Sets the text for what the pointer is over.
  void hover(String text) {
    _set(state.copyWith(hover: text));
    notifyListeners();
  }

  void _set(PanelState next) => state = next;

  int _step(int value, String delta, int low, int high) =>
      (value + int.parse(delta)).clamp(low, high);

  void _runMenu(int index) {
    final entries = state.menu;
    _set(state.copyWith(menu: const []));
    if (index < entries.length) entries[index].run();
  }

  void _tapSlot(ContainerId container, int slot) {
    final picked = state.picked;
    if (picked == null) return _pickOrAct(container, slot);
    _set(state.copyWith(picked: () => null));
    if (picked != (container: container, slot: slot)) {
      session.moveItem(picked.container, picked.slot, container, slot);
    }
  }

  /// A tap with nothing picked up. An open bank takes the stack, an open
  /// trade offers one of it, and otherwise it is picked up to be moved.
  void _pickOrAct(ContainerId container, int slot) {
    final stack = session.itemAt(container, slot);
    if (stack == null) return;
    final trade = session.playerTrade;
    if (container == ContainerId.inventory && session.bankOpen) {
      session.deposit(slot, 1);
    } else if (container == ContainerId.inventory && trade != null) {
      final offered = trade.mine
          .where((s) => s.item == stack.item)
          .fold(0, (sum, s) => sum + s.count);
      session.offer(stack.item, offered + 1);
    } else {
      _set(state.copyWith(picked: () => (container: container, slot: slot)));
    }
  }
}
