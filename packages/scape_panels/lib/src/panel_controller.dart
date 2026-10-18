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
      'tab': (a) => _set(_state.copyWith(tab: SideTab.values.byName(a[0]))),
      'chat': (a) => _set(_state.copyWith(chat: ChatTab.values.byName(a[0]))),
      'slot': (a) => _tapSlot(ContainerId.values.byName(a[0]), int.parse(a[1])),
      'menu': (a) => _runMenu(int.parse(a[0])),
      'menu-close': (_) => _set(_state.copyWith(menu: const [])),
      'volume': (a) => _set(
        _state.copyWith(volume: _step(_state.volume, a[0], 0, maxVolume)),
      ),
      'view': (a) => _set(
        _state.copyWith(
          viewDistance: _step(
            _state.viewDistance,
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
  PanelState _state = PanelState();
  late final Map<String, _Handler> _handlers;

  PanelState get state => _state;

  /// Tells the panels to redraw, after [GameSession.advance], because the
  /// session has no listener of its own. A picked-up stack whose slot the
  /// server has emptied is put down.
  void refresh() {
    final picked = _state.picked;
    if (picked != null &&
        session.itemAt(picked.container, picked.slot) == null) {
      _state = _state.copyWith(picked: () => null);
    }
    notifyListeners();
  }

  /// Runs what a button said, such as `slot:inventory:3`.
  void handle(String handler) {
    final parts = handler.split(':');
    _handlers[parts.first]?.call(parts.sublist(1));
  }

  /// Shows [entries] as the right-click menu, or hides it when empty.
  void openMenu(List<MenuEntry> entries) =>
      _set(_state.copyWith(menu: entries));

  /// Sets the text for what the pointer is over.
  void hover(String text) {
    if (text != _state.hover) _set(_state.copyWith(hover: text));
  }

  void _set(PanelState next) {
    _state = next;
    notifyListeners();
  }

  int _step(int value, String delta, int low, int high) =>
      (value + int.parse(delta)).clamp(low, high);

  void _runMenu(int index) {
    final entries = _state.menu;
    _set(_state.copyWith(menu: const []));
    if (index < entries.length) entries[index].run();
  }

  void _tapSlot(ContainerId container, int slot) {
    final picked = _state.picked;
    if (picked == null) return _pickOrAct(container, slot);
    _set(_state.copyWith(picked: () => null));
    if (picked != (container: container, slot: slot)) {
      session.moveItem(picked.container, picked.slot, container, slot);
    }
  }

  /// A tap with nothing picked up. An open bank takes one of the stack, an
  /// open trade offers one more of it, and otherwise it is picked up to be
  /// moved.
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
      _set(_state.copyWith(picked: () => (container: container, slot: slot)));
    }
  }
}
