import 'package:scape_core/scape_core.dart';

import 'accounts/accounts.dart';
import 'features/admin/ledger.dart';
import 'persistence/saves.dart';

/// What a login must pass before the host seats the player: the account when
/// the server has them, the ban, then the save.
final class Entrance {
  Entrance({required Saves saves, Accounts? accounts, Ledger? ledger})
    : _saves = saves,
      _accounts = accounts,
      _ledger = ledger;

  final Saves _saves;
  final Accounts? _accounts;
  final Ledger? _ledger;

  /// Null when [login] may join, or why it may not. [player] is the key.
  // F5: a login that is allowed must also make its account and load its save,
  // and one step would race the other.
  Future<LoginRefusal?> check(String player, Login login) async {
    final accounts = _accounts;
    if (accounts != null) {
      final why = login.create
          ? await accounts.create(player, login.password)
          : await accounts.verify(player, login.password);
      if (why != null) return why;
    }
    final why = await _screen(player);
    if (why != null) return why;
    await _saves.prepare(player);
    return null;
  }

  /// Why the ledger keeps [player] out: a ban, or a lock while their save is
  /// rolled back. The ban is checked after the password, so a wrong guess
  /// never learns who is banned.
  Future<LoginRefusal?> _screen(String player) async {
    final ledger = _ledger;
    if (ledger == null) return null;
    await ledger.load();
    if (ledger.active(SanctionKind.ban, player) != null) {
      return LoginRefusal.banned;
    }
    return ledger.isLocked(player) ? LoginRefusal.unavailable : null;
  }
}
