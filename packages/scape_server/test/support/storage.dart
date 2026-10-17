import 'dart:async';
import 'dart:typed_data';

import 'package:orblit_save/orblit_save.dart';
import 'package:scape_server/scape_server.dart';

/// A hash a test can wait for: the password bytes and the salt, mixed.
final class FastHasher implements PasswordHasher {
  /// When set, a hash waits for it.
  Completer<void>? gate;

  /// How many hashes were asked for.
  int calls = 0;

  @override
  Future<Uint8List> hash(String password, Uint8List salt) async {
    calls++;
    await gate?.future;
    final bytes = password.codeUnits;
    return Uint8List.fromList([
      for (var i = 0; i < 8; i++)
        (bytes[i % bytes.length] * 31 + salt[i % salt.length] + i) & 0xff,
    ]);
  }
}

/// A store a test can hold. A read finds the save first and then waits for
/// [readGate], so it can return what was true when it began. A write waits
/// for [writeGate].
final class GatedStore implements SaveStore {
  GatedStore(this.inner);

  final SaveStore inner;
  Completer<void>? readGate;
  Completer<void>? writeGate;

  @override
  Future<SaveRecord?> read(String key) async {
    final found = await inner.read(key);
    await readGate?.future;
    return found;
  }

  @override
  Future<void> write(String key, SaveRecord record) async {
    await writeGate?.future;
    await inner.write(key, record);
  }

  @override
  Future<bool> exists(String key) => inner.exists(key);

  @override
  Future<List<String>> keys() => inner.keys();
}

/// A clock that moves one second each time it is read.
DateTime Function() steppingClock() {
  var seconds = 0;
  return () => DateTime.utc(2026, 1, 1).add(Duration(seconds: seconds++));
}
