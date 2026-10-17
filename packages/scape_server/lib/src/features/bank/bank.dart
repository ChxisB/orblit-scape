import 'package:scape_core/scape_core.dart';

import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../objects/objects.dart';
import '../windows/windows.dart';

const Map<Outcome, String> _notices = {
  Outcome.wontFit: 'There is no room for that.',
  Outcome.nothingThere: 'There is nothing in that slot.',
  Outcome.noSuchSlot: 'There is nothing in that slot.',
};

/// The bank booth. Using one opens the player's bank, and while it is open
/// they can move stacks between the bank and the inventory.
final class Bank {
  Bank._(this._services, this._bags, this._windows);

  /// Adds the bank to [registry]. The `'bank'` object action opens it.
  static void register(
    Registry registry,
    Services services, {
    required Interactions interactions,
    required Items bags,
    required Windows windows,
  }) {
    final feature = Bank._(services, bags, windows);
    interactions.addObjectAction('bank', feature._open);
    registry.addIntentHandler(feature._onIntent);
  }

  final Services _services;
  final Items _bags;
  final Windows _windows;

  void _open(String player, PlacedObject object) {
    _windows.open(
      player,
      WindowKind.bank,
      ref: 0,
      reach: Beside(object.footprint),
    );
    _services.hosting.tell(player, const BankOpen());
  }

  void _onIntent(String player, Intent intent) {
    if (_windows.of(player)?.kind != WindowKind.bank) return;
    final inventory = _bags.container(player, ContainerId.inventory);
    final bank = _bags.container(player, ContainerId.bank);
    if (inventory == null || bank == null) return;
    switch (intent) {
      case Deposit(:final slot, :final count, :final tab):
        _report(player, inventory.transfer(slot, bank, count: count, tab: tab));
      case Withdraw(:final slot, :final count):
        _report(player, bank.transfer(slot, inventory, count: count));
      default:
    }
  }

  void _report(String player, Outcome outcome) {
    final notice = _notices[outcome];
    if (notice != null) _services.hosting.tell(player, Notice(notice));
  }
}
