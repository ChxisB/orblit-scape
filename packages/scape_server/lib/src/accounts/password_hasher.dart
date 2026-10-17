import 'dart:isolate';
import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';

/// Turns a password and a salt into the digest an account keeps.
abstract interface class PasswordHasher {
  Future<Uint8List> hash(String password, Uint8List salt);
}

/// Argon2id with the memory, passes and lanes the OWASP guide gives as the
/// floor for a server. It runs in its own isolate, so a login that takes a
/// second of work never stalls the tick.
final class Argon2idHasher implements PasswordHasher {
  const Argon2idHasher();

  @override
  Future<Uint8List> hash(String password, Uint8List salt) =>
      Isolate.run(() => _derive(password, salt));
}

Future<Uint8List> _derive(String password, Uint8List salt) async {
  final algorithm = Argon2id(
    memory: 19456,
    parallelism: 1,
    iterations: 2,
    hashLength: 32,
  );
  final key = await algorithm.deriveKeyFromPassword(
    password: password,
    nonce: salt,
  );
  return Uint8List.fromList(await key.extractBytes());
}
