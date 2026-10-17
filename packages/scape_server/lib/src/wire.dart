import 'dart:typed_data';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

import 'inbox.dart';

/// What the wire needs from the host. Declared here, beside the code that
/// calls it.
abstract interface class WireLink {
  bool isLive(String key);

  void send(String key, Uint8List body);
}

/// Turns what clients send into logins and intents, and counts what it
/// refuses.
final class Wire {
  Wire({required WireLink link, required Inbox inbox})
    : _link = link,
      _inbox = inbox;

  final WireLink _link;
  final Inbox _inbox;
  final Map<BadMessage, int> _refused = {};

  /// Messages understood after login.
  int understood = 0;

  /// Logins sent by a player who is already logged in.
  int unexpected = 0;

  /// Bytes that were not a message, by why. The host's budget refuses a
  /// message before it gets here and counts that itself.
  Map<BadMessage, int> get refused => Map.unmodifiable(_refused);

  /// The name is the whole of the identity until accounts exist, so any name
  /// nobody is logged in under is welcome.
  Admission admit(Uint8List credentials) {
    final decoded = decodeClientMessage(credentials);
    if (decoded case Understood(message: Login(:final name))) {
      final key = nameKey(name);
      return _link.isLive(key)
          ? Refuse(LoginRefusal.nameTaken.name)
          : Admit(key);
    }
    if (decoded case Unreadable(:final why)) _refuse(why);
    return Refuse(LoginRefusal.unreadable.name);
  }

  void onJoin(String key) {
    _link.send(key, encodeServerMessage(LoginOk(key)));
  }

  void onGone(String key, GoneReason reason) => _inbox.forget(key);

  void onMessage(String key, Uint8List body) {
    switch (decodeClientMessage(body)) {
      case Understood(message: final Intent intent):
        understood++;
        _inbox.add(key, intent);
      case Understood(message: Login()):
        unexpected++;
      case Unreadable(:final why):
        _refuse(why);
    }
  }

  void _refuse(BadMessage why) => _refused[why] = (_refused[why] ?? 0) + 1;
}
