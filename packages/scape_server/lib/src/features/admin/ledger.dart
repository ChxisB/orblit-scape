import 'dart:convert';
import 'dart:typed_data';

import 'package:orblit_save/orblit_save.dart';

import '../../persistence/keys.dart';

enum SanctionKind {
  mute('muted'),
  ban('banned');

  const SanctionKind(this.done);

  /// What a player is called once it is on them.
  final String done;
}

/// Why a moderator held a player back, and until when.
final class Sanction {
  const Sanction({required this.by, required this.reason, this.until});

  final String by;
  final String reason;

  /// When it ends, or null when it has no end.
  final DateTime? until;

  bool isActive(DateTime now) {
    final end = until;
    return end == null || now.isBefore(end);
  }
}

const int _format = 1;

const FormatException _damaged = FormatException(
  'The sanction ledger is damaged',
);

/// Who is muted or banned, and who is locked out for a moment. Mutes and bans
/// are one record in the save store, so they outlast a restart. Locks are
/// kept in memory only.
///
/// [load] must finish before the first read, because a restart starts with
/// nothing. A login does that on its way in, so no one is online before it.
final class Ledger {
  Ledger({required SaveQueue queue, required DateTime Function() now})
    : _queue = queue,
      _now = now;

  final SaveQueue _queue;
  final DateTime Function() _now;
  final Map<String, Sanction> _mutes = {};
  final Map<String, Sanction> _bans = {};
  final Set<String> _locked = {};
  Future<void>? _loaded;
  bool _ready = false;

  /// Reads the record once. Throws [FormatException] when it is damaged, so
  /// a login is refused and the record is never written over.
  Future<void> load() async {
    final run = _loaded ??= _read();
    try {
      await run;
    } on Object {
      _loaded = null;
      rethrow;
    }
  }

  Future<void> _read() async {
    final record = await _queue.read(ledgerKey);
    if (record != null) _decode(record.bytes);
    _ready = true;
  }

  Map<String, Sanction> _of(SanctionKind kind) => switch (kind) {
    SanctionKind.mute => _mutes,
    SanctionKind.ban => _bans,
  };

  /// The sanction on [player] that has not ended, or null.
  Sanction? active(SanctionKind kind, String player) {
    final found = _of(kind)[player];
    return found != null && found.isActive(_now()) ? found : null;
  }

  /// Puts a sanction on [player] for [lasting], or with no end when that is
  /// null, and saves. Throws [StateError] before [load] has finished, since
  /// the save would drop what the store holds.
  void add(
    SanctionKind kind,
    String player, {
    required String by,
    required String reason,
    Duration? lasting,
  }) {
    final until = lasting == null ? null : _now().add(lasting);
    _of(kind)[player] = Sanction(by: by, reason: reason, until: until);
    _save();
  }

  void lift(SanctionKind kind, String player) {
    _of(kind).remove(player);
    _save();
  }

  /// Keeps [player] from logging in until [unlock].
  void lock(String player) => _locked.add(player);

  void unlock(String player) => _locked.remove(player);

  bool isLocked(String player) => _locked.contains(player);

  void _save() {
    if (!_ready) throw StateError('The ledger was changed before it loaded');
    _queue.save(ledgerKey, _encode(), Custom('sanction'));
  }

  Uint8List _encode() {
    final now = _now();
    Map<String, Object?> part(Map<String, Sanction> held) => {
      for (final MapEntry(:key, :value) in held.entries)
        if (value.isActive(now))
          key: {
            'by': value.by,
            'reason': value.reason,
            'until': value.until?.millisecondsSinceEpoch,
          },
    };
    final json = {'v': _format, 'mute': part(_mutes), 'ban': part(_bans)};
    return Uint8List.fromList(utf8.encode(jsonEncode(json)));
  }

  void _decode(Uint8List bytes) {
    final json = jsonDecode(utf8.decode(bytes));
    if (json case {'v': _format, 'mute': final mutes, 'ban': final bans}) {
      final read = (_readPart(mutes), _readPart(bans));
      _mutes.addAll(read.$1);
      _bans.addAll(read.$2);
      return;
    }
    throw _damaged;
  }

  static Map<String, Sanction> _readPart(Object? json) {
    if (json is! Map<String, Object?>) {
      throw _damaged;
    }
    return {
      for (final MapEntry(:key, :value) in json.entries)
        key: _readSanction(value),
    };
  }

  static Sanction _readSanction(Object? json) {
    if (json case {
      'by': final String by,
      'reason': final String reason,
      'until': final int? until,
    }) {
      return Sanction(
        by: by,
        reason: reason,
        until: until == null
            ? null
            : DateTime.fromMillisecondsSinceEpoch(until, isUtc: true),
      );
    }
    throw _damaged;
  }
}
