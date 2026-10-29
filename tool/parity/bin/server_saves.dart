import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_save/orblit_save.dart';
import 'package:scape_parity_tool/fixture.dart';
import 'package:scape_server/scape_server.dart';
// ignore: implementation_imports, Accounts is not exported
import 'package:scape_server/src/accounts/accounts.dart';
// ignore: implementation_imports, Ledger is not exported
import 'package:scape_server/src/features/admin/ledger.dart';
// ignore: implementation_imports, tradeKeyOf is not exported
import 'package:scape_server/src/persistence/keys.dart' show tradeKeyOf;
// ignore: implementation_imports, the journal codec is not exported
import 'package:scape_server/src/persistence/trade_journal.dart';

typedef _Json = Map<String, Object?>;

/// A stand-in for Argon2id: the salt, then the password's UTF-8. The C# can
/// make the same bytes without a hash library, and the `hash` cases check the
/// real one.
final class _PlainHasher implements PasswordHasher {
  const _PlainHasher();

  @override
  Future<Uint8List> hash(String password, Uint8List salt) async =>
      Uint8List.fromList([...salt, ...utf8.encode(password)]);
}

final DateTime _start = DateTime.utc(2026, 10, 29, 12);

/// [_start] plus [seconds], as the milliseconds a record holds.
int _at(int seconds) =>
    _start.add(Duration(seconds: seconds)).millisecondsSinceEpoch;

SaveQueue _queue(DateTime Function() clock) =>
    SaveQueue.inline(MemorySaveStore(), clock: clock, onFailure: (_, _) {});

/// What an async call gave, or that it threw, as [attempt] writes it.
Future<_Json> _settled(Future<Object?> Function() body) async {
  try {
    return {'value': await body()};
  } on Object {
    return {'throws': true};
  }
}

_Json _key(String name) => {
  'op': 'key',
  'name': name,
  'expect': [saveKeyOf(name), accountKeyOf(name)],
};

_Json _trade(String a, String b) => {
  'op': 'trade',
  'a': a,
  'b': b,
  'expect': tradeKeyOf(a, b),
};

/// Letters that sort apart by code unit: a space becomes a dot, and the dot,
/// hyphen, digits, capitals and underscore sit between them.
String _randomName(Random random) {
  const letters = 'abyz0189 _-AZ';
  return String.fromCharCodes([
    for (var i = random.nextInt(12); i >= 0; i--)
      letters.codeUnitAt(random.nextInt(letters.length)),
  ]);
}

_Json _journalWrite(List<(String, List<int>)> states) => {
  'op': 'journalWrite',
  'states': [
    for (final (name, bytes) in states) [name, hex(bytes)],
  ],
  'expect': utf8.decode(
    encodeJournal({
      for (final (name, bytes) in states) name: Uint8List.fromList(bytes),
    }),
  ),
};

_Json _journalRead(List<int> bytes) => {
  'op': 'journalRead',
  'bytes': hex(bytes),
  'expect': attempt(
    () => [
      for (final MapEntry(:key, :value) in decodeJournal(
        Uint8List.fromList(bytes),
      ).entries)
        [key, hex(value)],
    ],
  ),
};

List<int> _randomBytes(Random random, int most) => [
  for (var i = random.nextInt(most + 1); i > 0; i--) random.nextInt(256),
];

Future<_Json> _hash(String password, List<int> salt) async => {
  'op': 'hash',
  'password': password,
  'salt': hex(salt),
  'expect': hex(
    await const Argon2idHasher().hash(password, Uint8List.fromList(salt)),
  ),
};

/// An account made for [name] in a store that holds [taken], if any. The
/// salt is the first sixteen draws of `Random(seed)`, which the case lists.
Future<_Json> _create(
  String name,
  String password, {
  String? taken,
  int seed = 1,
}) async {
  final queue = _queue(() => _start);
  if (taken != null) queue.save(taken, Uint8List(0), Custom('taken'));
  final dice = Random(seed);
  final accounts = Accounts(
    queue: queue,
    hasher: const _PlainHasher(),
    random: Random(seed),
  );
  final refusal = await accounts.create(name, password);
  final record = refusal == null ? await queue.read(accountKeyOf(name)) : null;
  return {
    'op': 'create',
    'name': name,
    'password': password,
    'taken': taken,
    'salt': hex([for (var i = 0; i < 16; i++) dice.nextInt(256)]),
    'expect': {
      'refusal': refusal?.name,
      'record': switch (record) {
        final found? => utf8.decode(found.bytes),
        null => null,
      },
    },
  };
}

/// A login as `a` against [record], or against no account when it is null.
Future<_Json> _verify(List<int>? record, String password) async {
  final queue = _queue(() => _start);
  if (record != null) {
    queue.save(
      accountKeyOf('a'),
      Uint8List.fromList(record),
      Custom('account'),
    );
  }
  final accounts = Accounts(
    queue: queue,
    hasher: const _PlainHasher(),
    random: Random(1),
  );
  return {
    'op': 'verify',
    'record': record == null ? null : hex(record),
    'password': password,
    'expect': await _settled(
      () async => (await accounts.verify('a', password))?.name,
    ),
  };
}

/// An account record as text, with the plain hash of [password] under the
/// salt 0 to 15.
String _account(String password, {String version = '1'}) {
  final salt = [for (var i = 0; i < 16; i++) i];
  final hash = [...salt, ...utf8.encode(password)];
  return '{"version":$version,"salt":"${b64(salt)}","hash":"${b64(hash)}"}';
}

_Json _add(
  int seconds,
  String kind,
  String player, {
  String by = 'mod',
  String reason = 'spam',
  int? lasting,
}) => {
  'at': _at(seconds),
  'add': kind,
  'player': player,
  'by': by,
  'reason': reason,
  'lasting': lasting,
};

_Json _lift(int seconds, String kind, String player) => {
  'at': _at(seconds),
  'lift': kind,
  'player': player,
};

void _apply(Ledger ledger, _Json step) {
  switch (step) {
    case {
      'add': final String kind,
      'player': final String player,
      'by': final String by,
      'reason': final String reason,
      'lasting': final int? lasting,
    }:
      ledger.add(
        SanctionKind.values.byName(kind),
        player,
        by: by,
        reason: reason,
        lasting: lasting == null ? null : Duration(milliseconds: lasting),
      );
    case {'lift': final String kind, 'player': final String player}:
      ledger.lift(SanctionKind.values.byName(kind), player);
  }
}

/// The ledger record after each of [steps], each at its own time.
Future<_Json> _ledgerWrite(List<_Json> steps) async {
  var now = _start;
  final queue = _queue(() => now);
  final ledger = Ledger(queue: queue, now: () => now);
  await ledger.load();
  final records = <String>[];
  for (final step in steps) {
    now = DateTime.fromMillisecondsSinceEpoch(step['at']! as int, isUtc: true);
    _apply(ledger, step);
    records.add(utf8.decode((await queue.read(ledgerKey))!.bytes));
  }
  return {'op': 'ledgerWrite', 'steps': steps, 'expect': records};
}

const List<String> _players = ['a', 'b', 'c d', 'x'];

/// What the ledger holds on each of [_players], mutes first, at [seconds].
Future<_Json> _ledgerRead(List<int> bytes, {int seconds = 0}) async {
  final now = _start.add(Duration(seconds: seconds));
  final queue = _queue(() => now)
    ..save(ledgerKey, Uint8List.fromList(bytes), Custom('sanction'));
  final ledger = Ledger(queue: queue, now: () => now);
  return {
    'op': 'ledgerRead',
    'bytes': hex(bytes),
    'now': now.millisecondsSinceEpoch,
    'players': _players,
    'expect': await _settled(() async {
      await ledger.load();
      return [
        for (final kind in SanctionKind.values)
          for (final player in _players)
            switch (ledger.active(kind, player)) {
              final s? => [s.by, s.reason, s.until?.millisecondsSinceEpoch],
              null => null,
            },
      ];
    }),
  };
}

Future<_Json> _ledgerText(String text, {int seconds = 0}) =>
    _ledgerRead(utf8.encode(text), seconds: seconds);

/// A ledger text with one mute on `a` whose `until` is written as [until].
String _muteUntil(String until) =>
    '{"v":1,"mute":{"a":{"by":"m","reason":"r","until":$until}},"ban":{}}';

List<_Json> _randomSteps(Random random) {
  const reasons = ['spam', 'said "hi"', 'é 🙂', '<b>&+\'', 'a\u2028b'];
  var seconds = 0;
  final steps = <_Json>[];
  for (var i = random.nextInt(6); i >= 0; i--) {
    seconds += random.nextInt(120);
    final kind = SanctionKind.values[random.nextInt(2)].name;
    final player = _players[random.nextInt(3)];
    steps.add(
      random.nextInt(4) == 0
          ? _lift(seconds, kind, player)
          : _add(
              seconds,
              kind,
              player,
              reason: reasons[random.nextInt(reasons.length)],
              lasting: random.nextInt(3) == 0 ? null : random.nextInt(180000),
            ),
    );
  }
  return steps;
}

List<_Json> _keyCases() {
  final random = Random(29);
  return [
    for (final name in ['a', 'ab cd', 'a b c', 'x_y', 'x-y', '12345'])
      _key(name),
    _key('abcdefghijkl'),
    _trade('a', 'b'),
    _trade('b', 'a'),
    _trade('ab', 'c'),
    _trade('a', 'bc'),
    _trade('a b', 'a'),
    _trade('a b', 'a-b'),
    _trade('a_b', 'a-b'),
    _trade('a1', 'a b'),
    _trade('B', 'a'),
    for (var i = 0; i < 40; i++)
      _trade(_randomName(random), _randomName(random)),
  ];
}

List<_Json> _journalCases() {
  final random = Random(31);
  List<int> text(String s) => utf8.encode(s);
  return [
    _journalWrite([
      ('a', [1, 2, 3]),
      ('b c', []),
    ]),
    _journalWrite([
      ('z', [0xfb, 0xff, 0xbe]),
      ('a', [0x3e, 0x3f]),
    ]),
    for (var i = 0; i < 20; i++)
      _journalWrite([
        (_randomName(random).toLowerCase(), _randomBytes(random, 40)),
        (_randomName(random).toLowerCase(), _randomBytes(random, 40)),
      ]),
    _journalRead(text('{"a":"AQID","b c":""}')),
    _journalRead(text('{"a":"AQ==","a":"Ag==","b":""}')),
    _journalRead(text('{"a":"AQ","b":""}')),
    _journalRead(text('{"a":"-_8=","b":"+/8="}')),
    _journalRead(text('{"a":"AQ ID","b":""}')),
    _journalRead(text('{"a":"!!!!","b":""}')),
    _journalRead(text(' { "a" : "" , "b" : "" } ')),
    _journalRead([0xef, 0xbb, 0xbf, ...text('{"a":"","b":""}')]),
    _journalRead(text('{"a":""}')),
    _journalRead(text('{"a":"","b":"","c":""}')),
    _journalRead(text('{"A":"","b":""}')),
    _journalRead(text('{"a.b":"","b":""}')),
    _journalRead(text('{"abcdefghijklm":"","b":""}')),
    _journalRead(text('{"":"","b":""}')),
    _journalRead(text('{"a":1,"b":""}')),
    _journalRead(text('{"a":null,"b":""}')),
    _journalRead(text('["a","b"]')),
    _journalRead(text('not json')),
    _journalRead([0xff, 0xfe]),
    _journalRead([]),
  ];
}

Future<List<_Json>> _accountCases() async {
  final random = Random(37);
  List<int> text(String s) => utf8.encode(s);
  return [
    for (final (password, salt) in [
      ('password1', List.filled(16, 0)),
      ('password1', [for (var i = 0; i < 16; i++) i]),
      ('correct horse battery staple', List.filled(16, 0xff)),
      ('pässwörd', [for (var i = 0; i < 16; i++) random.nextInt(256)]),
      ('密码密码密码', [for (var i = 0; i < 16; i++) random.nextInt(256)]),
      ('🙂🙂🙂🙂', [for (var i = 0; i < 16; i++) random.nextInt(256)]),
      ('a' * 1000, [for (var i = 0; i < 16; i++) random.nextInt(256)]),
      ('', List.filled(16, 0)),
    ])
      await _hash(password, salt),
    await _create('a', 'password1'),
    await _create('b c', 'ééééé', seed: 2),
    await _create('d', 'éééé', seed: 3),
    await _create('d', 'seven77'),
    await _create('d', '12345678', seed: 4),
    await _create('e', 'password1', taken: accountKeyOf('e')),
    await _create('e', 'password1', taken: saveKeyOf('e')),
    await _create('e', 'password1', taken: accountKeyOf('f')),
    await _verify(text(_account('password1')), 'password1'),
    await _verify(text(_account('password1')), 'password2'),
    await _verify(null, 'password1'),
    await _verify(text(_account('password1', version: '2')), 'password1'),
    await _verify(text(_account('password1', version: '1.0')), 'password1'),
    await _verify(text(_account('password1', version: '"1"')), 'password1'),
    await _verify(
      text('{"version":1,"salt":"","hash":"","more":1}'),
      'password1',
    ),
    await _verify(text('{"version":1,"salt":"","hash":"AA=="}'), 'x'),
    await _verify(text('{"version":1,"salt":"","hash":"eA"}'), 'x'),
    await _verify(text('{"version":1,"salt":"","hash":"eA=="}'), 'x'),
    await _verify(text('{"version":1,"salt":"","hash":"!!"}'), 'x'),
    await _verify(text('{"version":1,"salt":1,"hash":"eA=="}'), 'x'),
    await _verify(text('{"version":1,"salt":""}'), 'x'),
    await _verify(
      text('{"version":1,"salt":"","hash":"AA==","hash":"eA=="}'),
      'x',
    ),
    await _verify([0xef, 0xbb, 0xbf, ...text(_account('x'))], 'x'),
    await _verify(text('[1]'), 'x'),
    await _verify(text('not json'), 'x'),
    await _verify([0xff], 'x'),
    await _verify([], 'x'),
  ];
}

Future<List<_Json>> _ledgerCases() async {
  final random = Random(41);
  final soon = _at(60);
  return [
    await _ledgerWrite([_add(0, 'mute', 'a', lasting: 60000)]),
    await _ledgerWrite([_add(0, 'ban', 'b', by: 'x', reason: 'cheat')]),
    await _ledgerWrite([
      _add(0, 'mute', 'a', lasting: 60000),
      _add(120, 'ban', 'b'),
    ]),
    await _ledgerWrite([_add(0, 'mute', 'a', lasting: 0)]),
    await _ledgerWrite([
      _add(0, 'ban', 'a'),
      _add(1, 'ban', 'b'),
      _lift(2, 'ban', 'a'),
      _add(3, 'ban', 'c d'),
      _add(4, 'ban', 'b', reason: 'again'),
    ]),
    await _ledgerWrite([
      _add(0, 'mute', 'a', reason: 'said "hi" \\ <b>&+\' é 🙂 a b'),
    ]),
    await _ledgerWrite([_lift(0, 'mute', 'a')]),
    for (var i = 0; i < 20; i++) await _ledgerWrite(_randomSteps(random)),
    await _ledgerText('{"v":1,"mute":{},"ban":{}}'),
    await _ledgerText(_muteUntil('null')),
    await _ledgerText(_muteUntil('$soon')),
    await _ledgerText(_muteUntil('$soon'), seconds: 60),
    await _ledgerText(_muteUntil('$soon'), seconds: 120),
    await _ledgerText(_muteUntil('$soon.0')),
    await _ledgerText(_muteUntil('1.5')),
    await _ledgerText(_muteUntil('1e15')),
    await _ledgerText(_muteUntil('"$soon"')),
    await _ledgerText(_muteUntil('-1')),
    await _ledgerText(_muteUntil('253402300800000')),
    await _ledgerText(_muteUntil('8640000000000000')),
    await _ledgerText(_muteUntil('8640000000000001')),
    await _ledgerText('{"v":1,"mute":{"a":{"by":"m","reason":"r"}},"ban":{}}'),
    await _ledgerText(
      '{"v":1,"mute":{"a":{"by":"m","reason":1,"until":null}},"ban":{}}',
    ),
    await _ledgerText(
      '{"v":1,"mute":{"a":{"reason":"r","until":null}},'
      '"ban":{}}',
    ),
    await _ledgerText(
      '{"v":1,"mute":{"a":{"by":"m","reason":"r","until":null,'
      '"more":1}},"ban":{"b":{"by":"n","reason":"s","until":null}},'
      '"more":1}',
    ),
    await _ledgerText(
      '{"v":1,"mute":{"a":{"by":"m","reason":"r","until":null},'
      '"a":{"by":"n","reason":"s","until":null}},"ban":{}}',
    ),
    await _ledgerText('{"v":2,"mute":{},"ban":{}}'),
    await _ledgerText('{"v":1.0,"mute":{},"ban":{}}'),
    await _ledgerText('{"v":"1","mute":{},"ban":{}}'),
    await _ledgerText('{"mute":{},"ban":{}}'),
    await _ledgerText('{"v":1,"mute":{}}'),
    await _ledgerText('{"v":1,"mute":[],"ban":{}}'),
    await _ledgerText('[]'),
    await _ledgerText('not json'),
    await _ledgerRead([0xef, 0xbb, 0xbf, ...utf8.encode(_muteUntil('null'))]),
    await _ledgerRead([0xff]),
    await _ledgerRead([]),
  ];
}

Future<void> main(List<String> args) async {
  writeFixture(
    root: args.single,
    package: 'scape_server',
    name: 'saves',
    cases: [
      ..._keyCases(),
      ..._journalCases(),
      ...await _accountCases(),
      ...await _ledgerCases(),
    ],
  );
}
