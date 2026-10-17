import 'package:scape_core/scape_core.dart';

import 'accounts/accounts.dart';
import 'persistence/saves.dart';

/// What a login must pass before the host seats the player: the account when
/// the server has them, then the save.
final class Entrance {
  Entrance({required Saves saves, Accounts? accounts})
    : _saves = saves,
      _accounts = accounts;

  final Saves _saves;
  final Accounts? _accounts;

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
    await _saves.prepare(player);
    return null;
  }
}
