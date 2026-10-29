import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

/// The items of scape_core's test support, which `ItemSupport` in
/// Scape.Core.Tests holds too. Reading a slot delta checks its items against
/// them.
final ItemSet _items = ItemSet(
  skills: SkillSet(
    curve: XpCurve(quadratic: 30, cubic: 4),
    skills: [SkillDef(id: 'fighting', name: 'Fighting', levelCap: 50)],
  ),
  items: [
    ItemDef(id: 1, name: 'Coins', stackable: true, value: 1),
    ItemDef(id: 2, name: 'Logs', value: 4),
    ItemDef(
      id: 3,
      name: 'Sword',
      equipSlot: 'weapon',
      needs: (skill: 'fighting', level: 5),
    ),
    ItemDef(id: 4, name: 'Helmet', equipSlot: 'head'),
    ItemDef(id: 5, name: 'Arrows', stackable: true, equipSlot: 'weapon'),
  ],
);

T _pick<T>(Random random, List<T> from) => from[random.nextInt(from.length)];

List<int> _tile(TileCoord tile) => [tile.x, tile.y, tile.plane];

List<List<int>> _stacks(List<ItemStack> stacks) => [
  for (final stack in stacks) [stack.item, stack.count],
];

/// A message as its class name, then its fields in the order the class
/// declares them. Enums travel by name and bytes as hex.
List<Object?> _client(ClientMessage m) => switch (m) {
  Login(:final name, :final password, :final create) => [
    'Login',
    name,
    password,
    create,
  ],
  Walk(:final target) => ['Walk', _tile(target)],
  Interact(:final kind, :final id, :final option) => [
    'Interact',
    kind,
    id,
    option,
  ],
  Chat(:final text) => ['Chat', text],
  Whisper(:final to, :final text) => ['Whisper', to, text],
  EditList(:final list, :final add, :final name) => [
    'EditList',
    list.name,
    add,
    name,
  ],
  Buy(:final item, :final count) => ['Buy', item, count],
  Sell(:final item, :final count) => ['Sell', item, count],
  Deposit(:final slot, :final count, :final tab) => [
    'Deposit',
    slot,
    count,
    tab,
  ],
  Withdraw(:final slot, :final count) => ['Withdraw', slot, count],
  MoveItem(:final from, :final fromSlot, :final to, :final toSlot) => [
    'MoveItem',
    from.name,
    fromSlot,
    to.name,
    toSlot,
  ],
  TradeOffer(:final item, :final count) => ['TradeOffer', item, count],
  TradeAccept() => ['TradeAccept'],
  TradeDecline() => ['TradeDecline'],
  Cast(:final spell, :final target) => ['Cast', spell, target],
  TogglePrayer(:final prayer) => ['TogglePrayer', prayer],
  Continue() => ['Continue'],
  Choose(:final index) => ['Choose', index],
};

List<Object?> _server(ServerMessage m) => switch (m) {
  LoginOk(:final name) => ['LoginOk', name],
  Avatar(:final networkId) => ['Avatar', networkId],
  PrivateState(:final delta) => ['PrivateState', hex(delta)],
  Notice(:final text) => ['Notice', text],
  Hit(:final target, :final damage) => ['Hit', target, damage],
  Projectile(:final from, :final to, :final ticks, :final style) => [
    'Projectile',
    from,
    to,
    ticks,
    style.name,
  ],
  PrayerView(:final points, :final max, :final active) => [
    'PrayerView',
    points,
    max,
    active,
  ],
  Said(:final speaker, :final text) => ['Said', speaker, text],
  Whispered(:final from, :final text) => ['Whispered', from, text],
  SocialLists(:final friends, :final ignored) => [
    'SocialLists',
    [
      for (final friend in friends) [friend.name, friend.online],
    ],
    ignored,
  ],
  ShopView(:final npc, :final lines) => [
    'ShopView',
    npc,
    [
      for (final line in lines) [line.item, line.count],
    ],
  ],
  BankOpen() => ['BankOpen'],
  WindowClosed() => ['WindowClosed'],
  TradeView() => [
    'TradeView',
    m.partner,
    _stacks(m.mine),
    _stacks(m.theirs),
    m.confirming,
    m.youAccepted,
    m.theyAccepted,
  ],
  ConversationLine(:final speaker, :final text) => [
    'ConversationLine',
    speaker,
    text,
  ],
  ConversationChoices(:final options) => ['ConversationChoices', options],
};

/// Every client message, each at the edges of what its fields may hold.
List<ClientMessage> _clients() => [
  Login('a'),
  Login('Zed 99', password: 'pässwörd'),
  Login('abcdefghijkl', password: 'x' * maxPasswordBytes, create: true),
  Walk(TileCoord(0, 0, 0)),
  Walk(TileCoord(12345, 321, 1)),
  Walk(TileCoord(worldSize - 1, worldSize - 1, planeCount - 1)),
  Interact(kind: 0, id: 0, option: 0),
  Interact(kind: 0xFF, id: 0xFFFFFFFF, option: 0xFF),
  Chat('x'),
  Chat('café ☃ 🐍'),
  Chat('é' * (maxTextBytes ~/ 2)),
  Chat('x' * maxTextBytes),
  Whisper(to: 'Ana', text: 'hi'),
  Whisper(to: 'abcdefghijkl', text: 'y' * maxTextBytes),
  EditList(list: ListKind.friends, add: true, name: 'Ana'),
  EditList(list: ListKind.ignored, add: false, name: 'b'),
  Buy(item: 1, count: 1),
  Buy(item: maxItemId, count: maxStack),
  Sell(item: 7, count: 300),
  Deposit(slot: 0, count: 1, tab: 0),
  Deposit(slot: 0xFFFF, count: maxStack, tab: 0xFF),
  Withdraw(slot: 0, count: 1),
  Withdraw(slot: 0xFFFF, count: maxStack),
  MoveItem(
    from: ContainerId.inventory,
    fromSlot: 0,
    to: ContainerId.equipment,
    toSlot: 10,
  ),
  MoveItem(
    from: ContainerId.equipment,
    fromSlot: 0xFFFF,
    to: ContainerId.inventory,
    toSlot: 0xFFFF,
  ),
  TradeOffer(item: 1, count: 0),
  TradeOffer(item: maxItemId, count: maxStack),
  const TradeAccept(),
  const TradeDecline(),
  Cast(spell: 0, target: 0),
  Cast(spell: maxSpellId, target: 0xFFFFFFFF),
  TogglePrayer(1),
  TogglePrayer(maxPrayerId),
  const Continue(),
  Choose(0),
  Choose(0xFF),
];

/// Every server message, each at the edges of what its fields may hold.
List<ServerMessage> _servers() => [
  LoginOk('a'),
  LoginOk('abcdefghijkl'),
  Avatar(0),
  Avatar(0xFFFFFFFF),
  PrivateState(Uint8List(0)),
  PrivateState(Uint8List.fromList([2, 0, 0xFF])),
  PrivateState(
    Uint8List.fromList([for (var i = 0; i < maxDeltaBytes; i++) i * 7]),
  ),
  Notice('Welcome'),
  Notice('é' * (maxTextBytes ~/ 2)),
  Hit(target: 0, damage: 0),
  Hit(target: 0xFFFFFFFF, damage: 0xFFFF),
  Projectile(from: 0, to: 0, ticks: 1, style: CombatStyle.melee),
  Projectile(from: 1, to: 2, ticks: 3, style: CombatStyle.ranged),
  Projectile(
    from: 0xFFFFFFFF,
    to: 0xFFFFFFFF,
    ticks: 0xFF,
    style: CombatStyle.magic,
  ),
  PrayerView(points: 0, max: 0, active: []),
  PrayerView(points: 0xFFFF, max: 0xFFFF, active: [1, maxPrayerId, 7]),
  Said(speaker: 0, text: 'x'),
  Said(speaker: 0xFFFFFFFF, text: 'x' * maxTextBytes),
  Whispered(from: 'Ana', text: 'hi'),
  SocialLists(friends: [], ignored: []),
  SocialLists(
    friends: [
      for (var i = 0; i < maxListEntries; i++)
        (name: 'Friend $i', online: i.isOdd),
    ],
    ignored: [for (var i = 0; i < maxListEntries; i++) 'abcdefghij$i'],
  ),
  ShopView(npc: 0, lines: []),
  ShopView(
    npc: 0xFFFF,
    lines: [
      for (var i = 0; i < maxShopLines; i++)
        (item: maxItemId - i * 1000, count: maxShopStock - i * 25000),
    ],
  ),
  const BankOpen(),
  const WindowClosed(),
  TradeView(
    partner: 'Ana',
    mine: [],
    theirs: [],
    confirming: false,
    youAccepted: false,
    theyAccepted: false,
  ),
  TradeView(
    partner: 'abcdefghijkl',
    mine: [for (var i = 1; i <= maxTradeLines; i++) ItemStack(i, maxStack - i)],
    theirs: [
      for (var i = 1; i <= maxTradeLines; i++) ItemStack(maxItemId - i, i),
    ],
    confirming: true,
    youAccepted: false,
    theyAccepted: true,
  ),
  ConversationLine(speaker: 'Guide', text: 'Hello'),
  ConversationLine(speaker: 's' * maxTextBytes, text: 't' * maxTextBytes),
  ConversationChoices(['Yes']),
  ConversationChoices([
    for (var i = 0; i < maxChoices; i++) '$i' * maxTextBytes,
  ]),
];

/// Text bytes on which UTF-8 readers differ: a byte order mark, which Dart
/// drops one of, and bytes a strict reader refuses and a lax one replaces.
const _utf8Edges = <List<int>>[
  [0xEF, 0xBB, 0xBF, 0x68, 0x69],
  [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x68, 0x69],
  [0xEF, 0xBB, 0xBF],
  [0xC0, 0xAF],
  [0xED, 0xA0, 0x80],
  [0xEF, 0xBF, 0xBF],
  [0xEF, 0xBF, 0xBE],
  [0xF4, 0x8F, 0xBF, 0xBF],
  [0xF4, 0x90, 0x80, 0x80],
  [0xF0, 0x9F, 0x90, 0x8D],
  [0xC2, 0x85],
  [0xC2, 0xA0],
  [0xE2, 0x80, 0xA8],
  [0x00],
  [0x7F],
  [0x20],
  [0xE2, 0x82],
  [0x80],
  [0xFF],
];

/// Each edge where a message holds text: a chat line, a login name, a
/// whisper's name and its text, and the server's notice and conversation
/// line. Both decoders read every one.
List<List<int>> _placed() => [
  for (final text in _utf8Edges) ...[
    [4, text.length, ...text],
    [1, text.length, ...text, 0, 0],
    [5, text.length, ...text, 2, 0x68, 0x69],
    [5, 3, 0x41, 0x6E, 0x61, text.length, ...text],
    [3, text.length, ...text],
    [12, 1, 0x47, text.length, ...text],
  ],
];

/// Flag and kind bytes past the values an encoder writes. Some readers refuse
/// them and some read only the bits they know.
const _flagEdges = <List<int>>[
  [1, 3, 0x41, 0x6E, 0x61, 2, 0],
  [6, 4, 3, 0x41, 0x6E, 0x61],
  [7, 1, 3, 0x41, 0x6E, 0x61, 2, 0],
  [7, 1, 3, 0x41, 0x6E, 0x61, 0xFF, 0],
  [14, 3, 0x41, 0x6E, 0x61, 0xF8, 0, 0],
  [14, 3, 0x41, 0x6E, 0x61, 0xFF, 0, 0],
  [15, 1, 0, 0, 0, 2, 0, 0, 0, 9, 2],
  [15, 1, 0, 0, 0, 2, 0, 0, 0, 9, 3],
  [15, 1, 0, 0, 0, 2, 0, 0, 0, 9, 0xFF],
];

/// [bytes] with one fault: cut short, one byte changed, or one byte more.
List<int> _damage(Random random, List<int> bytes) =>
    switch (random.nextInt(3)) {
      0 => bytes.sublist(0, random.nextInt(bytes.length)),
      1 => [...bytes]..[random.nextInt(bytes.length)] = random.nextInt(256),
      _ => [...bytes, random.nextInt(256)],
    };

/// A tag, most of them known, and up to a dozen random bytes.
List<int> _noise(Random random) => [
  random.nextInt(20),
  for (var i = random.nextInt(13); i > 0; i--) random.nextInt(256),
];

/// What a decoder made of some bytes: the message and the bytes it writes
/// for it, or why it refused them.
_Json _outcome<T>(
  Decoded<T> decoded,
  List<Object?> Function(T) describe,
  Uint8List Function(T) encode,
) => switch (decoded) {
  Understood(:final message) => {
    'message': describe(message),
    'bytes': hex(encode(message)),
  },
  Unreadable(:final why) => {'why': why.name},
};

_Json _read(String op, List<int> bytes) {
  final data = Uint8List.fromList(bytes);
  return {
    'op': op,
    'bytes': hex(data),
    'expect': {
      'client': _outcome(decodeClientMessage(data), _client, encodeClientMessage),
      'server': _outcome(decodeServerMessage(data), _server, encodeServerMessage),
    },
  };
}

/// A container with a random quarter of its slots mirrored full, and the
/// deltas for some of its slots. Now and then the slots are every slot, so a
/// bank splits.
_Json _encodeSlots(Random random) {
  final at = random.nextInt(ContainerId.values.length);
  final id = ContainerId.values[at];
  final bag = Container.forId(id, _items);
  final held = [
    for (var slot = 0; slot < bag.size; slot++)
      if (random.nextInt(4) == 0)
        [slot, 1 + random.nextInt(5), 1 + random.nextInt(maxStack)],
  ];
  bag.mirror([
    for (final row in held) (slot: row[0], stack: ItemStack(row[1], row[2])),
  ]);
  final slots = random.nextInt(5) == 0
      ? [for (var slot = 0; slot < bag.size; slot++) slot]
      : [for (var i = random.nextInt(12); i > 0; i--) random.nextInt(bag.size)];
  return {
    'op': 'encodeSlots',
    'in': at,
    'held': held,
    'slots': slots,
    'expect': [for (final delta in encodeSlots(id, bag, slots)) hex(delta)],
  };
}

/// A delta built from parts, each now and then one a reader must refuse: the
/// wrong tag, a container that does not exist, a slot past the last, an item
/// no set has, a count the item cannot hold, or a few bytes too many.
Uint8List _delta(Random random) {
  final entries = random.nextInt(4);
  final extra = random.nextInt(6) == 0 ? 1 + random.nextInt(7) : 0;
  final data = ByteData(2 + entries * 8 + extra)
    ..setUint8(0, random.nextInt(8) == 0 ? random.nextInt(4) : itemsDeltaTag)
    ..setUint8(1, random.nextInt(10) == 0 ? 3 : random.nextInt(3));
  for (var at = 2; at < 2 + entries * 8; at += 8) {
    data
      ..setUint16(at, _pick(random, const [0, 1, 27, 28, 799, 800, 0xFFFF]),
          Endian.little)
      ..setUint16(at + 2, _pick(random, const [0, 1, 1, 2, 3, 5, 99, 0xFFFF]),
          Endian.little)
      ..setUint32(
          at + 4,
          _pick(random,
              const [0, 1, 1, 2, 7, maxStack, 0x80000000, 0xFFFFFFFF]),
          Endian.little);
  }
  return data.buffer.asUint8List();
}

_Json _readSlots(Random random) {
  final delta = _delta(random);
  final read = tryDecodeSlots(delta, _items);
  return {
    'op': 'readSlots',
    'bytes': hex(delta),
    'expect': read == null
        ? null
        : [
            read.container.name,
            [
              for (final (:slot, :stack) in read.changes)
                [slot, if (stack != null) ...[stack.item, stack.count]],
            ],
          ],
  };
}

/// Skill and quest ids a JSON writer must take care with: accents, a
/// character past the basic plane, quotes, a backslash and control bytes.
const _ids = [
  'fighting',
  'magic',
  'Z',
  'é',
  '𝄞',
  'ﬀ',
  'a"b',
  r'a\b',
  'a/b',
  '\u0001',
  '\n',
  '\u007f',
  ' ',
  'a b',
];

Map<String, int> _wholes(Random random) => {
  for (final id in _ids)
    if (random.nextInt(3) == 0)
      id: random.nextBool()
          ? random.nextInt(1 << 32)
          : _pick(random, const [0, 1, maxSavedXp]),
};

List<String> _keys(Random random) => [
  for (var i = random.nextInt(5) == 0 ? maxListEntries : random.nextInt(4);
      i > 0;
      i--)
    'p${random.nextInt(1000)} q-r_',
];

PlayerState _player(Random random) => PlayerState(
  tile: TileCoord(
    random.nextInt(worldSize),
    random.nextInt(worldSize),
    random.nextInt(planeCount),
  ),
  skills: _wholes(random),
  quests: _wholes(random),
  containers: {
    for (final id in ContainerId.values)
      if (random.nextBool())
        id: [
          for (var slot = 0; slot < containerSize(id); slot++)
            random.nextInt(30) == 0
                ? ItemStack(1 + random.nextInt(maxItemId), 1 + random.nextInt(maxStack))
                : null,
        ],
  },
  friends: _keys(random),
  ignored: _keys(random),
);

/// A small save that reads, for the faults below to change one key of.
_Json _base() => {
  'version': playerStateVersion,
  'tile': [3, 4, 0],
  'skills': {'magic': 5},
  'quests': <String, int>{},
  'containers': {
    'inventory': [
      [0, 1, 10],
    ],
  },
  'friends': ['ana'],
  'ignored': <String>[],
};

/// One key of [_base] and the value it takes. Most are refused, and a few
/// that look wrong still read: an absent list, a key no reader knows.
final _faults = <(String, Object?)>[
  ('version', 3),
  ('version', 0),
  ('version', 1),
  ('version', '2'),
  ('version', 2.0),
  ('version', null),
  ('tile', [1, 2]),
  ('tile', [1, 2, 0, 0]),
  ('tile', [worldSize, 0, 0]),
  ('tile', [0, -1, 0]),
  ('tile', [0, 0, planeCount]),
  ('tile', [0.5, 0, 0]),
  ('tile', '0,0,0'),
  ('tile', null),
  ('skills', null),
  ('skills', <int>[]),
  ('skills', {'a': -1}),
  ('skills', {'a': maxSavedXp + 1}),
  ('skills', {'': 1}),
  ('skills', {'a': '1'}),
  ('skills', {'a': 1.0}),
  ('skills', {'a': null}),
  ('quests', {'q': maxSavedXp}),
  ('quests', 'q'),
  ('containers', null),
  ('containers', {'nope': <int>[]}),
  ('containers', {'Inventory': <int>[]}),
  ('containers', {'bank': <String, int>{}}),
  ('containers', {'bank': <int>[]}),
  ('containers', {
    'inventory': [
      [0, 1, 1],
      [0, 1, 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [inventorySize, 1, 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [-1, 1, 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [0, 0, 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [0, 1, 0],
    ],
  }),
  ('containers', {
    'inventory': [
      [0, maxItemId + 1, 1],
    ],
  }),
  ('containers', {
    'equipment': [
      [10, 1, maxStack],
    ],
  }),
  ('containers', {
    'equipment': [
      [0, 1, maxStack + 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [0, 1],
    ],
  }),
  ('containers', {
    'inventory': [
      [0, 1, 1, 1],
    ],
  }),
  ('friends', null),
  ('friends', ['Ana']),
  ('friends', ['ana', 'ana']),
  ('friends', [for (var i = 0; i <= maxListEntries; i++) 'p$i']),
  ('friends', [1]),
  ('friends', 'ana'),
  ('friends', [' ana']),
  ('ignored', ['abcdefghijklm']),
  ('extra', 1),
];

const _save = '{"version":2,"tile":[0,0,0]}';

/// Saves as text, each on a point where JSON readers differ: duplicate keys,
/// number spellings, escapes, lone surrogates, trailing commas and junk.
const _saveTexts = [
  _save,
  ' {\n "version" : 2 ,\t"tile" : [ 0 , 0 , 0 ] }\r\n',
  '{"version":2,"version":1,"tile":[0,0,0]}',
  '{"version":2,"tile":[0,0,0],"tile":[1,1,1]}',
  '{"version":1e0,"tile":[0,0,0]}',
  '{"version":2,"tile":[0,0,-0]}',
  '{"version":2,"tile":[0,0,007]}',
  '{"version":2,"tile":[0,0,NaN]}',
  '{"version":2,"tile":[0,0,0],"skills":{"a":9223372036854775807}}',
  '{"version":2,"tile":[0,0,0],"skills":{"a":9223372036854775808}}',
  '{"version":2,"tile":[0,0,0],"skills":{"a":-9223372036854775808}}',
  '{"version":2,"tile":[0,0,0],"skills":{"\\u00e9":1}}',
  '{"version":2,"tile":[0,0,0],"skills":{"\\ud834\\udd1e":1}}',
  '{"version":2,"tile":[0,0,0],"skills":{"\\ud800":1}}',
  '{"version":2,"tile":[0,0,0],"skills":{"\\/":1}}',
  '{"version":2,"tile":[0,0,0],"skills":{"a\tb":1}}',
  '{"version":2,"tile":[0,0,0],"friends":["ana",]}',
  '{"version":2,"tile":[0,0,0],}',
  '$_save x',
  '$_save{}',
  '$_save // a note',
  "{'version':2,'tile':[0,0,0]}",
  '[]',
  'null',
  '2',
  '"x"',
  '',
  '{',
];

/// Save bytes that are not plain UTF-8 text: byte order marks, and a byte
/// that is not UTF-8 inside a key.
final _saveBytes = <List<int>>[
  [0xEF, 0xBB, 0xBF, ...utf8.encode(_save)],
  [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, ...utf8.encode(_save)],
  [...utf8.encode(_save.replaceFirst('}', ',"skills":{"')), 0xFF, ...utf8.encode('":1}}')],
];

_Json _state(List<int> bytes) => {
  'op': 'state',
  'bytes': hex(bytes),
  'expect': attempt(
    () => hex(PlayerState.decode(Uint8List.fromList(bytes)).encode()),
  ),
};

void main(List<String> args) {
  final random = Random(7);
  final built = [
    for (final message in _clients()) encodeClientMessage(message),
    for (final message in _servers()) encodeServerMessage(message),
  ];
  writeFixture(
    root: args.single,
    package: 'scape_core',
    name: 'wire',
    cases: [
      for (final bytes in built) _read('built', bytes),
      for (final bytes in _placed()) _read('bytes', bytes),
      for (final bytes in _flagEdges) _read('bytes', bytes),
      for (final bytes in built) ...[
        _read('bytes', _damage(random, bytes)),
        _read('bytes', _damage(random, bytes)),
      ],
      _read('bytes', []),
      for (var i = 0; i < 200; i++) _read('bytes', _noise(random)),
      for (var i = 0; i < 24; i++) _encodeSlots(random),
      for (var i = 0; i < 120; i++) _readSlots(random),
      for (var i = 0; i < 16; i++) _state(_player(random).encode()),
      for (final (key, value) in _faults)
        _state(utf8.encode(jsonEncode({..._base(), key: value}))),
      for (final text in _saveTexts) _state(utf8.encode(text)),
      for (final bytes in _saveBytes) _state(bytes),
    ],
  );
}
