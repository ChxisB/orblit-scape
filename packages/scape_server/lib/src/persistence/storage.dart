import 'dart:math';

import 'package:orblit_save/orblit_save.dart';

import '../accounts/password_hasher.dart';

/// Where a server keeps its players and how it checks who they are.
///
/// Without a [hasher] there are no accounts and a name is the whole of an
/// identity, which suits a test or a private game. With one, a login names an
/// account and gives its password.
final class Storage {
  Storage({required this.queue, this.hasher, Random? salt, AutoSave? autosave})
    : salt = salt ?? Random.secure(),
      autosave =
          autosave ??
          AutoSave(perTick: 100, every: 500, jitter: 100, random: Random());

  final SaveQueue queue;
  final PasswordHasher? hasher;

  /// The dice for an account's salt.
  final Random salt;
  final AutoSave autosave;
}
