import 'dart:async';
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

/// What a login passes before the player joins, such as a password and a
/// save that may take a while to read. It answers null for a login that may
/// join, or why not. A throw is an `unavailable` refusal.
typedef LoginGate = Future<LoginRefusal?> Function(String key, Login login);

/// Turns what clients send into logins and intents, and counts what it
/// refuses.
final class Wire {
  Wire({required WireLink link, required Inbox inbox, LoginGate? gate})
    : _link = link,
      _inbox = inbox,
      _gate = gate;

  final WireLink _link;
  final Inbox _inbox;
  final LoginGate? _gate;
  final Map<BadMessage, int> _refused = {};

  /// Keys whose login is waiting on the gate. A second login for one of them
  /// would race the first, so it is turned away.
  final Set<String> _deciding = {};

  /// Messages understood after login.
  int understood = 0;

  /// Logins sent by a player who is already logged in.
  int unexpected = 0;

  /// Logins the gate failed on, so neither admitted nor refused for a reason
  /// the player could fix.
  int unavailable = 0;

  /// Bytes that were not a message, by why. The host's budget refuses a
  /// message before it gets here and counts that itself.
  Map<BadMessage, int> get refused => Map.unmodifiable(_refused);

  /// Without a gate the name is the whole of the identity, so any name nobody
  /// is logged in under is welcome. With one, the gate decides, and the host
  /// keeps ticking while it does.
  FutureOr<Admission> admit(Uint8List credentials) {
    final decoded = decodeClientMessage(credentials);
    if (decoded case Understood(message: final Login login)) {
      return _admitLogin(login);
    }
    if (decoded case Unreadable(:final why)) _refuse(why);
    return Refuse(LoginRefusal.unreadable.name);
  }

  FutureOr<Admission> _admitLogin(Login login) {
    final key = nameKey(login.name);
    final gate = _gate;
    if (_link.isLive(key)) return Refuse(LoginRefusal.nameTaken.name);
    if (gate == null) return Admit(key);
    if (!_deciding.add(key)) return Refuse(LoginRefusal.busy.name);
    return _decide(gate, key, login);
  }

  Future<Admission> _decide(LoginGate gate, String key, Login login) async {
    try {
      final why = await gate(key, login);
      return why == null ? Admit(key) : Refuse(why.name);
    } on Object {
      unavailable++;
      return Refuse(LoginRefusal.unavailable.name);
    } finally {
      _deciding.remove(key);
    }
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
