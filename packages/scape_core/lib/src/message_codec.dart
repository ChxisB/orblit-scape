import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'messages.dart';
import 'tile_coord.dart';

const int _login = 1;
const int _walk = 2;
const int _interact = 3;
const int _chat = 4;

const int _loginOk = 1;
const int _privateState = 2;
const int _notice = 3;
const int _avatar = 4;

/// The most bytes each message may take, by tag. A longer one is refused
/// before anything is read from it.
final Map<int, int> _clientCaps = {
  _login: 2 + maxNameChars,
  _walk: 6,
  _interact: 7,
  _chat: 2 + maxTextBytes,
};

final Map<int, int> _serverCaps = {
  _loginOk: 2 + maxNameChars,
  _privateState: 1 + maxDeltaBytes,
  _notice: 2 + maxTextBytes,
  _avatar: 5,
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
};

final Map<int, _Reader<ServerMessage>> _serverReaders = {
  _loginOk: _textMessage(LoginOk.new),
  _privateState: (b) => PrivateState(b.sublist(1)),
  _notice: _textMessage(Notice.new),
  _avatar: _readAvatar,
};

Uint8List encodeClientMessage(ClientMessage message) => switch (message) {
  Login(:final name) => _withText(_login, name),
  Walk(:final target) => _encodeWalk(target),
  Interact() => _encodeInteract(message),
  Chat(:final text) => _withText(_chat, text),
};

Uint8List encodeServerMessage(ServerMessage message) => switch (message) {
  LoginOk(:final name) => _withText(_loginOk, name),
  PrivateState(:final delta) => Uint8List.fromList([_privateState, ...delta]),
  Notice(:final text) => _withText(_notice, text),
  Avatar(:final networkId) => _encodeAvatar(networkId),
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
