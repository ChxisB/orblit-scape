import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'messages.dart';
import 'tile_coord.dart';

const int _login = 1;
const int _walk = 2;
const int _interact = 3;
const int _chat = 4;
const int _whisper = 5;
const int _editList = 6;

const int _loginOk = 1;
const int _privateState = 2;
const int _notice = 3;
const int _avatar = 4;
const int _said = 5;
const int _whispered = 6;
const int _socialLists = 7;
const int _hit = 8;

/// The most bytes each message may take, by tag. A longer one is refused
/// before anything is read from it.
final Map<int, int> _clientCaps = {
  _login: 2 + maxNameChars,
  _walk: 6,
  _interact: 7,
  _chat: 2 + maxTextBytes,
  _whisper: 3 + maxNameChars + maxTextBytes,
  _editList: 3 + maxNameChars,
};

final Map<int, int> _serverCaps = {
  _loginOk: 2 + maxNameChars,
  _privateState: 1 + maxDeltaBytes,
  _notice: 2 + maxTextBytes,
  _avatar: 5,
  _hit: 7,
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
  _login: _textMessage(Login.new),
  _walk: _readWalk,
  _interact: _readInteract,
  _chat: _textMessage(Chat.new),
  _whisper: _readWhisper,
  _editList: _readEditList,
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
};

Uint8List encodeClientMessage(ClientMessage message) => switch (message) {
  Login(:final name) => _withText(_login, name),
  Walk(:final target) => _encodeWalk(target),
  Interact() => _encodeInteract(message),
  Chat(:final text) => _withText(_chat, text),
  Whisper(:final to, :final text) => _withTwoTexts(_whisper, to, text),
  EditList() => _encodeEditList(message),
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
