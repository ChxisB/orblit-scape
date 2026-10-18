import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'combat.dart';
import 'container.dart';
import 'items.dart';
import 'messages.dart';
import 'shops.dart';
import 'tile_coord.dart';

const int _login = 1;
const int _walk = 2;
const int _interact = 3;
const int _chat = 4;
const int _whisper = 5;
const int _editList = 6;
const int _buy = 7;
const int _sell = 8;
const int _deposit = 9;
const int _withdraw = 10;
const int _continue = 11;
const int _choose = 12;
const int _tradeOffer = 13;
const int _tradeAccept = 14;
const int _tradeDecline = 15;
const int _cast = 16;
const int _togglePrayer = 17;
const int _moveItem = 18;

const int _loginOk = 1;
const int _privateState = 2;
const int _notice = 3;
const int _avatar = 4;
const int _said = 5;
const int _whispered = 6;
const int _socialLists = 7;
const int _hit = 8;
const int _shopView = 9;
const int _bankOpen = 10;
const int _windowClosed = 11;
const int _conversationLine = 12;
const int _conversationChoices = 13;
const int _tradeView = 14;
const int _projectile = 15;
const int _prayerView = 16;

/// The most bytes each message may take, by tag. A longer one is refused
/// before anything is read from it.
final Map<int, int> _clientCaps = {
  _login: 4 + maxNameChars + maxPasswordBytes,
  _walk: 6,
  _interact: 7,
  _chat: 2 + maxTextBytes,
  _whisper: 3 + maxNameChars + maxTextBytes,
  _editList: 3 + maxNameChars,
  _buy: 7,
  _sell: 7,
  _deposit: 8,
  _withdraw: 7,
  _continue: 1,
  _choose: 2,
  _tradeOffer: 7,
  _tradeAccept: 1,
  _tradeDecline: 1,
  _cast: 7,
  _togglePrayer: 2,
  _moveItem: 7,
};

final Map<int, int> _serverCaps = {
  _loginOk: 2 + maxNameChars,
  _privateState: 1 + maxDeltaBytes,
  _notice: 2 + maxTextBytes,
  _avatar: 5,
  _hit: 7,
  _projectile: 11,
  _prayerView: 6 + maxActivePrayers,
  _shopView: 4 + maxShopLines * 6,
  _bankOpen: 1,
  _windowClosed: 1,
  _conversationLine: 3 + 2 * maxTextBytes,
  _conversationChoices: 2 + maxChoices * (1 + maxTextBytes),
  _tradeView: 5 + maxNameChars + 2 * maxTradeLines * 6,
  _said: 6 + maxTextBytes,
  _whispered: 3 + maxNameChars + maxTextBytes,
  _socialLists:
      3 +
      maxListEntries * (2 + maxNameChars) +
      maxListEntries * (1 + maxNameChars),
};

/// The largest message a client may send. A host's inbound budget must let
/// one through.
final int clientMessageLimit = _clientCaps.values.reduce(max);

typedef _Reader<T> = T? Function(Uint8List bytes);

_Reader<T> _textMessage<T>(T Function(String text) make) => (bytes) {
  final text = _readText(bytes);
  return text == null ? null : make(text);
};

final Map<int, _Reader<ClientMessage>> _clientReaders = {
  _login: _readLogin,
  _walk: _readWalk,
  _interact: _readInteract,
  _chat: _textMessage(Chat.new),
  _whisper: _readWhisper,
  _editList: _readEditList,
  _buy: (b) => _readPair(b, (item, count) => Buy(item: item, count: count)),
  _sell: (b) => _readPair(b, (item, count) => Sell(item: item, count: count)),
  _deposit: _readDeposit,
  _withdraw: (b) =>
      _readPair(b, (slot, count) => Withdraw(slot: slot, count: count)),
  _continue: (b) => b.length == 1 ? const Continue() : null,
  _choose: (b) => b.length == 2 ? Choose(b[1]) : null,
  _tradeOffer: (b) =>
      _readPair(b, (item, count) => TradeOffer(item: item, count: count)),
  _tradeAccept: (b) => b.length == 1 ? const TradeAccept() : null,
  _tradeDecline: (b) => b.length == 1 ? const TradeDecline() : null,
  _cast: (b) =>
      _readPair(b, (spell, target) => Cast(spell: spell, target: target)),
  _togglePrayer: (b) => b.length == 2 ? TogglePrayer(b[1]) : null,
  _moveItem: _readMoveItem,
};

final Map<int, _Reader<ServerMessage>> _serverReaders = {
  _loginOk: _textMessage(LoginOk.new),
  _privateState: (b) => PrivateState(b.sublist(1)),
  _notice: _textMessage(Notice.new),
  _avatar: _readAvatar,
  _said: _readSaid,
  _whispered: _readWhispered,
  _socialLists: _readSocialLists,
  _hit: _readHit,
  _projectile: _readProjectile,
  _prayerView: _readPrayerView,
  _shopView: _readShopView,
  _bankOpen: (b) => b.length == 1 ? const BankOpen() : null,
  _windowClosed: (b) => b.length == 1 ? const WindowClosed() : null,
  _conversationLine: _readConversationLine,
  _conversationChoices: _readConversationChoices,
  _tradeView: _readTradeView,
};

Uint8List encodeClientMessage(ClientMessage message) => switch (message) {
  Login() => _encodeLogin(message),
  Walk(:final target) => _encodeWalk(target),
  Interact() => _encodeInteract(message),
  Chat(:final text) => _withText(_chat, text),
  Whisper(:final to, :final text) => _withTwoTexts(_whisper, to, text),
  EditList() => _encodeEditList(message),
  Buy(:final item, :final count) => _encodePair(_buy, item, count),
  Sell(:final item, :final count) => _encodePair(_sell, item, count),
  Deposit() => _encodeDeposit(message),
  Withdraw() => _encodeWithdraw(message),
  Continue() => Uint8List.fromList([_continue]),
  Choose(:final index) => Uint8List.fromList([_choose, index]),
  TradeOffer(:final item, :final count) => _encodePair(
    _tradeOffer,
    item,
    count,
  ),
  TradeAccept() => Uint8List.fromList([_tradeAccept]),
  TradeDecline() => Uint8List.fromList([_tradeDecline]),
  Cast(:final spell, :final target) => _encodePair(_cast, spell, target),
  TogglePrayer(:final prayer) => Uint8List.fromList([_togglePrayer, prayer]),
  MoveItem() => _encodeMoveItem(message),
};

Uint8List encodeServerMessage(ServerMessage message) => switch (message) {
  LoginOk(:final name) => _withText(_loginOk, name),
  PrivateState(:final delta) => Uint8List.fromList([_privateState, ...delta]),
  Notice(:final text) => _withText(_notice, text),
  Avatar(:final networkId) => _encodeAvatar(networkId),
  Said() => _encodeSaid(message),
  Whispered(:final from, :final text) => _withTwoTexts(_whispered, from, text),
  SocialLists() => _encodeSocialLists(message),
  Hit() => _encodeHit(message),
  Projectile() => _encodeProjectile(message),
  PrayerView() => _encodePrayerView(message),
  ShopView() => _encodeShopView(message),
  BankOpen() => Uint8List.fromList([_bankOpen]),
  WindowClosed() => Uint8List.fromList([_windowClosed]),
  ConversationLine(:final speaker, :final text) => _withTwoTexts(
    _conversationLine,
    speaker,
    text,
  ),
  ConversationChoices(:final options) => _encodeChoices(options),
  TradeView() => _encodeTradeView(message),
};

Decoded<ClientMessage> decodeClientMessage(Uint8List bytes) =>
    _decode(bytes, _clientCaps, _clientReaders);

Decoded<ServerMessage> decodeServerMessage(Uint8List bytes) =>
    _decode(bytes, _serverCaps, _serverReaders);

Decoded<T> _decode<T>(
  Uint8List bytes,
  Map<int, int> caps,
  Map<int, _Reader<T>> readers,
) {
  if (bytes.isEmpty) return const Unreadable(BadMessage.empty);
  final cap = caps[bytes[0]];
  if (cap == null) return const Unreadable(BadMessage.unknownTag);
  if (bytes.length > cap) return const Unreadable(BadMessage.tooLarge);
  try {
    final message = readers[bytes[0]]!(bytes);
    return message == null
        ? const Unreadable(BadMessage.malformed)
        : Understood(message);
  } on ArgumentError {
    // The message classes validate their own fields. A client's bytes that
    // break one of those rules are malformed, not an error here.
    return const Unreadable(BadMessage.malformed);
  } on FormatException {
    return const Unreadable(BadMessage.malformed);
  }
}

/// Tag, a length byte, then that many bytes of UTF-8.
Uint8List _withText(int tag, String text) {
  final body = utf8.encode(text);
  return Uint8List(2 + body.length)
    ..[0] = tag
    ..[1] = body.length
    ..setRange(2, 2 + body.length, body);
}

/// Tag, the name, a byte that is 1 to create the account, then the password.
/// The name and the password each have a length byte.
Uint8List _encodeLogin(Login message) {
  final name = utf8.encode(message.name);
  final password = utf8.encode(message.password);
  return Uint8List.fromList([
    _login,
    name.length,
    ...name,
    if (message.create) 1 else 0,
    password.length,
    ...password,
  ]);
}

Login? _readLogin(Uint8List bytes) {
  final name = _textAt(bytes, 1);
  if (name == null || name.next >= bytes.length) return null;
  final flag = bytes[name.next];
  final password = _textAt(bytes, name.next + 1);
  if (flag > 1 || password == null || password.next != bytes.length) {
    return null;
  }
  return Login(name.text, password: password.text, create: flag == 1);
}

/// The string in a tag, length and text message, or null when the length
/// byte does not match what follows.
String? _readText(Uint8List bytes) {
  if (bytes.length < 2 || bytes.length != 2 + bytes[1]) return null;
  return utf8.decode(Uint8List.sublistView(bytes, 2));
}

Uint8List _encodeWalk(TileCoord target) {
  final bytes = Uint8List(6)..[0] = _walk;
  ByteData.sublistView(bytes)
    ..setUint16(1, target.x, Endian.little)
    ..setUint16(3, target.y, Endian.little)
    ..setUint8(5, target.plane);
  return bytes;
}

Walk? _readWalk(Uint8List bytes) {
  if (bytes.length != 6) return null;
  final data = ByteData.sublistView(bytes);
  return Walk(
    TileCoord(
      data.getUint16(1, Endian.little),
      data.getUint16(3, Endian.little),
      data.getUint8(5),
    ),
  );
}

Uint8List _encodeInteract(Interact message) {
  final bytes = Uint8List(7)..[0] = _interact;
  ByteData.sublistView(bytes)
    ..setUint8(1, message.kind)
    ..setUint32(2, message.id, Endian.little)
    ..setUint8(6, message.option);
  return bytes;
}

Interact? _readInteract(Uint8List bytes) {
  if (bytes.length != 7) return null;
  final data = ByteData.sublistView(bytes);
  return Interact(
    kind: data.getUint8(1),
    id: data.getUint32(2, Endian.little),
    option: data.getUint8(6),
  );
}

Uint8List _encodeAvatar(int networkId) {
  final bytes = Uint8List(5)..[0] = _avatar;
  ByteData.sublistView(bytes).setUint32(1, networkId, Endian.little);
  return bytes;
}

Avatar? _readAvatar(Uint8List bytes) {
  if (bytes.length != 5) return null;
  return Avatar(ByteData.sublistView(bytes).getUint32(1, Endian.little));
}

/// Tag, then two texts each with a length byte.
Uint8List _withTwoTexts(int tag, String first, String second) {
  final a = utf8.encode(first);
  final b = utf8.encode(second);
  return Uint8List.fromList([tag, a.length, ...a, b.length, ...b]);
}

/// Reads a text with a length byte at [at], or null when it runs past the
/// end. [next] is where the following field starts.
({String text, int next})? _textAt(Uint8List bytes, int at) {
  if (at >= bytes.length) return null;
  final end = at + 1 + bytes[at];
  if (end > bytes.length) return null;
  return (
    text: utf8.decode(Uint8List.sublistView(bytes, at + 1, end)),
    next: end,
  );
}

({String first, String second})? _readTwoTexts(Uint8List bytes) {
  final first = _textAt(bytes, 1);
  if (first == null) return null;
  final second = _textAt(bytes, first.next);
  if (second == null || second.next != bytes.length) return null;
  return (first: first.text, second: second.text);
}

Whisper? _readWhisper(Uint8List bytes) {
  final texts = _readTwoTexts(bytes);
  return texts == null ? null : Whisper(to: texts.first, text: texts.second);
}

Whispered? _readWhispered(Uint8List bytes) {
  final texts = _readTwoTexts(bytes);
  return texts == null
      ? null
      : Whispered(from: texts.first, text: texts.second);
}

Uint8List _encodeEditList(EditList message) {
  final name = utf8.encode(message.name);
  final flags =
      (message.list == ListKind.ignored ? 1 : 0) | (message.add ? 2 : 0);
  return Uint8List.fromList([_editList, flags, name.length, ...name]);
}

EditList? _readEditList(Uint8List bytes) {
  if (bytes.length < 3 || bytes[1] > 3 || bytes.length != 3 + bytes[2]) {
    return null;
  }
  return EditList(
    list: bytes[1] & 1 == 0 ? ListKind.friends : ListKind.ignored,
    add: bytes[1] & 2 != 0,
    name: utf8.decode(Uint8List.sublistView(bytes, 3)),
  );
}

Uint8List _encodeSaid(Said message) {
  final text = utf8.encode(message.text);
  final bytes = Uint8List(6 + text.length)
    ..[0] = _said
    ..[5] = text.length
    ..setRange(6, 6 + text.length, text);
  ByteData.sublistView(bytes).setUint32(1, message.speaker, Endian.little);
  return bytes;
}

Said? _readSaid(Uint8List bytes) {
  if (bytes.length < 6 || bytes.length != 6 + bytes[5]) return null;
  return Said(
    speaker: ByteData.sublistView(bytes).getUint32(1, Endian.little),
    text: utf8.decode(Uint8List.sublistView(bytes, 6)),
  );
}

Uint8List _encodeHit(Hit message) {
  final bytes = Uint8List(7)..[0] = _hit;
  ByteData.sublistView(bytes)
    ..setUint32(1, message.target, Endian.little)
    ..setUint16(5, message.damage, Endian.little);
  return bytes;
}

Hit? _readHit(Uint8List bytes) {
  if (bytes.length != 7) return null;
  final data = ByteData.sublistView(bytes);
  return Hit(
    target: data.getUint32(1, Endian.little),
    damage: data.getUint16(5, Endian.little),
  );
}

/// Tag, the two entity ids, the ticks and the index of the style.
Uint8List _encodeProjectile(Projectile message) {
  final bytes = Uint8List(11)
    ..[0] = _projectile
    ..[9] = message.ticks
    ..[10] = message.style.index;
  ByteData.sublistView(bytes)
    ..setUint32(1, message.from, Endian.little)
    ..setUint32(5, message.to, Endian.little);
  return bytes;
}

Projectile? _readProjectile(Uint8List bytes) {
  if (bytes.length != 11 || bytes[10] >= CombatStyle.values.length) return null;
  final data = ByteData.sublistView(bytes);
  return Projectile(
    from: data.getUint32(1, Endian.little),
    to: data.getUint32(5, Endian.little),
    ticks: bytes[9],
    style: CombatStyle.values[bytes[10]],
  );
}

/// Tag, the points and the most as u16, the number of prayers on, then their
/// ids.
Uint8List _encodePrayerView(PrayerView message) {
  final bytes = Uint8List(6 + message.active.length)
    ..[0] = _prayerView
    ..[5] = message.active.length
    ..setRange(6, 6 + message.active.length, message.active);
  ByteData.sublistView(bytes)
    ..setUint16(1, message.points, Endian.little)
    ..setUint16(3, message.max, Endian.little);
  return bytes;
}

PrayerView? _readPrayerView(Uint8List bytes) {
  if (bytes.length < 6 || bytes.length != 6 + bytes[5]) return null;
  final data = ByteData.sublistView(bytes);
  return PrayerView(
    points: data.getUint16(1, Endian.little),
    max: data.getUint16(3, Endian.little),
    active: bytes.sublist(6),
  );
}

Uint8List _encodeSocialLists(SocialLists message) {
  final out = BytesBuilder()..addByte(_socialLists);
  out.addByte(message.friends.length);
  for (final friend in message.friends) {
    final name = utf8.encode(friend.name);
    out
      ..addByte(name.length)
      ..add(name)
      ..addByte(friend.online ? 1 : 0);
  }
  out.addByte(message.ignored.length);
  for (final ignored in message.ignored) {
    final name = utf8.encode(ignored);
    out
      ..addByte(name.length)
      ..add(name);
  }
  return out.takeBytes();
}

SocialLists? _readSocialLists(Uint8List bytes) {
  if (bytes.length < 3) return null;
  final friends = <Friend>[];
  var at = 2;
  for (var i = 0; i < bytes[1]; i++) {
    final name = _textAt(bytes, at);
    if (name == null || name.next >= bytes.length) return null;
    friends.add((name: name.text, online: bytes[name.next] == 1));
    at = name.next + 1;
  }
  if (at >= bytes.length) return null;
  final count = bytes[at];
  at++;
  final ignored = <String>[];
  for (var i = 0; i < count; i++) {
    final name = _textAt(bytes, at);
    if (name == null) return null;
    ignored.add(name.text);
    at = name.next;
  }
  return at == bytes.length
      ? SocialLists(friends: friends, ignored: ignored)
      : null;
}

/// Tag, a u16, then a u32. A trade is an item and a count, and a withdrawal a
/// slot and a count.
Uint8List _encodePair(int tag, int small, int count) {
  final bytes = Uint8List(7)..[0] = tag;
  ByteData.sublistView(bytes)
    ..setUint16(1, small, Endian.little)
    ..setUint32(3, count, Endian.little);
  return bytes;
}

/// Reads what [_encodePair] wrote.
T? _readPair<T>(Uint8List bytes, T Function(int small, int count) make) {
  if (bytes.length != 7) return null;
  final data = ByteData.sublistView(bytes);
  return make(
    data.getUint16(1, Endian.little),
    data.getUint32(3, Endian.little),
  );
}

Uint8List _encodeWithdraw(Withdraw message) =>
    _encodePair(_withdraw, message.slot, message.count);

Uint8List _encodeDeposit(Deposit message) => Uint8List.fromList([
  ..._encodePair(_deposit, message.slot, message.count),
  message.tab,
]);

Deposit? _readDeposit(Uint8List bytes) {
  if (bytes.length != 8) return null;
  final data = ByteData.sublistView(bytes);
  return Deposit(
    slot: data.getUint16(1, Endian.little),
    count: data.getUint32(3, Endian.little),
    tab: bytes[7],
  );
}

Uint8List _encodeMoveItem(MoveItem message) {
  final bytes = Uint8List(7)
    ..[0] = _moveItem
    ..[1] = message.from.index
    ..[2] = message.to.index;
  ByteData.sublistView(bytes)
    ..setUint16(3, message.fromSlot, Endian.little)
    ..setUint16(5, message.toSlot, Endian.little);
  return bytes;
}

MoveItem? _readMoveItem(Uint8List bytes) {
  if (bytes.length != 7 ||
      bytes[1] >= ContainerId.values.length ||
      bytes[2] >= ContainerId.values.length) {
    return null;
  }
  final data = ByteData.sublistView(bytes);
  return MoveItem(
    from: ContainerId.values[bytes[1]],
    fromSlot: data.getUint16(3, Endian.little),
    to: ContainerId.values[bytes[2]],
    toSlot: data.getUint16(5, Endian.little),
  );
}

Uint8List _encodeShopView(ShopView message) {
  final bytes = Uint8List(4 + message.lines.length * 6)
    ..[0] = _shopView
    ..[3] = message.lines.length;
  final data = ByteData.sublistView(bytes)
    ..setUint16(1, message.npc, Endian.little);
  var at = 4;
  for (final (:item, :count) in message.lines) {
    data
      ..setUint16(at, item, Endian.little)
      ..setUint32(at + 2, count, Endian.little);
    at += 6;
  }
  return bytes;
}

ShopView? _readShopView(Uint8List bytes) {
  if (bytes.length < 4 || bytes.length != 4 + bytes[3] * 6) return null;
  final data = ByteData.sublistView(bytes);
  return ShopView(
    npc: data.getUint16(1, Endian.little),
    lines: [
      for (var at = 4; at < bytes.length; at += 6)
        (
          item: data.getUint16(at, Endian.little),
          count: data.getUint32(at + 2, Endian.little),
        ),
    ],
  );
}

/// Tag, the partner's name with a length byte, a flags byte (1 confirming,
/// 2 you accepted, 4 they accepted), the number of lines on each side, then
/// six bytes for each line: your lines first.
Uint8List _encodeTradeView(TradeView message) {
  final name = utf8.encode(message.partner);
  final lines = [...message.mine, ...message.theirs];
  final bytes = Uint8List(5 + name.length + lines.length * 6)
    ..[0] = _tradeView
    ..[1] = name.length
    ..setRange(2, 2 + name.length, name);
  final flags =
      (message.confirming ? 1 : 0) |
      (message.youAccepted ? 2 : 0) |
      (message.theyAccepted ? 4 : 0);
  final at = 2 + name.length;
  bytes
    ..[at] = flags
    ..[at + 1] = message.mine.length
    ..[at + 2] = message.theirs.length;
  final data = ByteData.sublistView(bytes);
  for (final (i, stack) in lines.indexed) {
    data
      ..setUint16(at + 3 + i * 6, stack.item, Endian.little)
      ..setUint32(at + 5 + i * 6, stack.count, Endian.little);
  }
  return bytes;
}

TradeView? _readTradeView(Uint8List bytes) {
  if (bytes.length < 2) return null;
  final at = 2 + bytes[1];
  if (bytes.length < at + 3) return null;
  final mine = bytes[at + 1];
  final theirs = bytes[at + 2];
  if (bytes.length != at + 3 + (mine + theirs) * 6) return null;
  final data = ByteData.sublistView(bytes);
  final stacks = [
    for (var i = 0; i < mine + theirs; i++)
      ItemStack(
        data.getUint16(at + 3 + i * 6, Endian.little),
        data.getUint32(at + 5 + i * 6, Endian.little),
      ),
  ];
  final flags = bytes[at];
  return TradeView(
    partner: utf8.decode(Uint8List.sublistView(bytes, 2, at)),
    mine: stacks.sublist(0, mine),
    theirs: stacks.sublist(mine),
    confirming: flags & 1 != 0,
    youAccepted: flags & 2 != 0,
    theyAccepted: flags & 4 != 0,
  );
}

ConversationLine? _readConversationLine(Uint8List bytes) {
  final texts = _readTwoTexts(bytes);
  return texts == null
      ? null
      : ConversationLine(speaker: texts.first, text: texts.second);
}

Uint8List _encodeChoices(List<String> options) {
  final out = BytesBuilder()
    ..addByte(_conversationChoices)
    ..addByte(options.length);
  for (final option in options) {
    final text = utf8.encode(option);
    out
      ..addByte(text.length)
      ..add(text);
  }
  return out.takeBytes();
}

ConversationChoices? _readConversationChoices(Uint8List bytes) {
  if (bytes.length < 2) return null;
  final options = <String>[];
  var at = 2;
  for (var i = 0; i < bytes[1]; i++) {
    final text = _textAt(bytes, at);
    if (text == null) return null;
    options.add(text.text);
    at = text.next;
  }
  return at == bytes.length ? ConversationChoices(options) : null;
}
