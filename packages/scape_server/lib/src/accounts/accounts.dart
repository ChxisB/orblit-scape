import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_save/orblit_save.dart';
import 'package:scape_core/scape_core.dart';

import '../persistence/keys.dart';
import 'password_hasher.dart';

const int _saltBytes = 16;
const int _recordVersion = 1;

/// A name and a salted, slow hash of a password for each player. The record
/// goes through the same [SaveQueue] as saves, so the tick never waits on the
/// disk for it and history keeps what it held.
///
/// A check holds one of [maxInFlight] places while it reads and hashes. When
/// they are all taken, the next login hears `busy` at once, so a flood of
/// logins costs a refusal each and not a hash each.
// shortcut: a wrong login costs the same as a right one and nothing counts
// failures per name, so a guesser is slowed only by the hash. Add a lockout
// when the server is public.
final class Accounts {
  Accounts({
    required SaveQueue queue,
    required PasswordHasher hasher,
    required Random random,
    this.maxInFlight = 4,
  }) : _queue = queue,
       _hasher = hasher,
       _random = random {
    if (maxInFlight < 1) {
      throw ArgumentError.value(maxInFlight, 'maxInFlight', 'is below one');
    }
  }

  final int maxInFlight;
  final SaveQueue _queue;
  final PasswordHasher _hasher;
  final Random _random;
  int _inFlight = 0;

  /// Makes the account for [player], who is a key, or says why not. A
  /// password under [minPasswordBytes] is weak, and a name that has an
  /// account is taken.
  Future<LoginRefusal?> create(String player, String password) async {
    if (utf8.encode(password).length < minPasswordBytes) {
      return LoginRefusal.weakPassword;
    }
    return _limited(() async {
      final key = accountKeyOf(player);
      if (await _queue.exists(key)) return LoginRefusal.accountExists;
      final salt = Uint8List.fromList([
        for (var i = 0; i < _saltBytes; i++) _random.nextInt(256),
      ]);
      final digest = await _hasher.hash(password, salt);
      _queue.save(key, _encode(salt, digest), Custom('account'));
      return null;
    });
  }

  /// Checks [password] against the account of [player], or says why not. An
  /// account that does not exist is a bad login too, so a guesser cannot
  /// tell names that are taken from names that are free.
  Future<LoginRefusal?> verify(String player, String password) =>
      _limited(() async {
        final record = await _queue.read(accountKeyOf(player));
        if (record == null) return LoginRefusal.badLogin;
        final (:salt, :digest) = _decode(record.bytes);
        final again = await _hasher.hash(password, salt);
        return _same(digest, again) ? null : LoginRefusal.badLogin;
      });

  Future<LoginRefusal?> _limited(Future<LoginRefusal?> Function() work) async {
    if (_inFlight >= maxInFlight) return LoginRefusal.busy;
    _inFlight++;
    try {
      return await work();
    } finally {
      _inFlight--;
    }
  }
}

Uint8List _encode(Uint8List salt, Uint8List digest) => Uint8List.fromList(
  utf8.encode(
    jsonEncode({
      'version': _recordVersion,
      'salt': base64.encode(salt),
      'hash': base64.encode(digest),
    }),
  ),
);

/// Reads what [_encode] wrote. Throws [FormatException] for anything else.
({Uint8List salt, Uint8List digest}) _decode(Uint8List bytes) {
  try {
    final data = jsonDecode(utf8.decode(bytes));
    if (data case {
      'version': _recordVersion,
      'salt': final String salt,
      'hash': final String hash,
    }) {
      return (salt: base64.decode(salt), digest: base64.decode(hash));
    }
  } on FormatException {
    // Falls through to the one error below.
  }
  throw const FormatException('Not an account record');
}

/// Compares without stopping at the first difference.
bool _same(Uint8List a, Uint8List b) {
  if (a.length != b.length) return false;
  var difference = 0;
  for (var i = 0; i < a.length; i++) {
    difference |= a[i] ^ b[i];
  }
  return difference == 0;
}
