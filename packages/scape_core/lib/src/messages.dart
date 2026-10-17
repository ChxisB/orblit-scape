import 'dart:convert';
import 'dart:typed_data';

import 'tile_coord.dart';

/// Letters and digits first, then up to eleven more with spaces, underscores
/// and hyphens inside, and no space at the end.
final RegExp _name = RegExp(
  r'^[A-Za-z0-9]([A-Za-z0-9 _-]{0,10}[A-Za-z0-9_-])?$',
);

const int maxNameChars = 12;

/// The most a line of chat or a notice may take once encoded as UTF-8.
const int maxTextBytes = 240;

/// The most names a friends list or an ignore list may hold.
const int maxListEntries = 50;

/// The most a [PrivateState] delta may hold.
const int maxDeltaBytes = 4096;

bool isValidName(String name) => _name.hasMatch(name);

/// What two names must share to be the same player: their letters, whatever
/// the case.
String nameKey(String name) => name.toLowerCase();

/// A line a player may read: not empty, within [maxTextBytes], and free of
/// control characters.
bool isValidText(String text) {
  if (text.isEmpty || utf8.encode(text).length > maxTextBytes) return false;
  return !text.runes.any((r) => r < 0x20 || r == 0x7f);
}

/// A message a client sends the server.
sealed class ClientMessage {
  const ClientMessage();
}

/// The credentials in a join. A name is all it takes until accounts exist.
final class Login extends ClientMessage {
  Login(this.name) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
  }

  final String name;
}

/// What a player asks to do. The server turns it into an action.
sealed class Intent extends ClientMessage {
  const Intent();
}

final class Walk extends Intent {
  const Walk(this.target);

  final TileCoord target;
}

/// Use the thing [id] names, in the way [option] picks. What [kind] and
/// [option] mean belongs to the interactions that read them.
final class Interact extends Intent {
  Interact({required this.kind, required this.id, required this.option}) {
    _inRange(kind, 'kind', 0xFF);
    _inRange(id, 'id', 0xFFFFFFFF);
    _inRange(option, 'option', 0xFF);
  }

  final int kind;
  final int id;
  final int option;
}

final class Chat extends Intent {
  Chat(this.text) {
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid chat text');
    }
  }

  final String text;
}

/// A private message to the player called [to], wherever they are.
final class Whisper extends Intent {
  Whisper({required this.to, required this.text}) {
    if (!isValidName(to)) {
      throw ArgumentError.value(to, 'to', 'is not a valid name');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid chat text');
    }
  }

  final String to;
  final String text;
}

/// The two lists a player keeps of other players.
enum ListKind { friends, ignored }

/// Puts [name] on a list, or takes it off.
final class EditList extends Intent {
  EditList({required this.list, required this.add, required this.name}) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
  }

  final ListKind list;
  final bool add;
  final String name;
}

/// A message the server sends a client.
sealed class ServerMessage {
  const ServerMessage();
}

/// The login was taken. [name] is the name the player is known by.
final class LoginOk extends ServerMessage {
  LoginOk(this.name) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
  }

  final String name;
}

/// Which replicated entity is the player's own, by network id. The client
/// follows it with the camera.
final class Avatar extends ServerMessage {
  Avatar(this.networkId) {
    if (networkId < 0 || networkId > 0xFFFFFFFF) {
      throw ArgumentError.value(networkId, 'networkId', 'does not fit a u32');
    }
  }

  final int networkId;
}

/// A change to what only the owner sees, such as items and skills. What the
/// bytes mean belongs to the feature that wrote them.
final class PrivateState extends ServerMessage {
  PrivateState(this.delta) {
    if (delta.length > maxDeltaBytes) {
      throw ArgumentError.value(delta.length, 'delta', 'is over the cap');
    }
  }

  final Uint8List delta;
}

final class Notice extends ServerMessage {
  Notice(this.text) {
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final String text;
}

/// A line of public chat, as the listener hears it. The client draws it over
/// the entity [speaker] names.
final class Said extends ServerMessage {
  Said({required this.speaker, required this.text}) {
    if (speaker < 0 || speaker > 0xFFFFFFFF) {
      throw ArgumentError.value(speaker, 'speaker', 'does not fit a u32');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final int speaker;
  final String text;
}

/// A private message from the player called [from].
final class Whispered extends ServerMessage {
  Whispered({required this.from, required this.text}) {
    if (!isValidName(from)) {
      throw ArgumentError.value(from, 'from', 'is not a valid name');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final String from;
  final String text;
}

/// A friend and whether they are logged in.
typedef Friend = ({String name, bool online});

/// A player's friends, with who is online, and the names they ignore. It is
/// sent whole whenever any of it changes.
final class SocialLists extends ServerMessage {
  SocialLists({required List<Friend> friends, required List<String> ignored})
    : friends = List.unmodifiable(friends),
      ignored = List.unmodifiable(ignored) {
    if (friends.length > maxListEntries || ignored.length > maxListEntries) {
      throw ArgumentError('A list holds at most $maxListEntries names.');
    }
    for (final name in [...friends.map((f) => f.name), ...ignored]) {
      if (!isValidName(name)) {
        throw ArgumentError.value(name, 'name', 'is not a valid name');
      }
    }
  }

  final List<Friend> friends;
  final List<String> ignored;
}

/// Why a login was refused. It reaches the client as the text of the
/// goodbye the host sends a connection it turns away, because a refused
/// connection is never a client and so cannot be sent a message.
enum LoginRefusal {
  /// A player is already logged in under that name.
  nameTaken,

  /// The bytes were not a login.
  unreadable;

  static LoginRefusal? fromWire(String reason) => values.asNameMap()[reason];
}

/// Why bytes were not turned into a message.
enum BadMessage { empty, unknownTag, tooLarge, malformed }

/// What a decoder made of some bytes. It never throws, since the bytes come
/// from the other end of a connection.
sealed class Decoded<T> {
  const Decoded();
}

final class Understood<T> extends Decoded<T> {
  const Understood(this.message);

  final T message;
}

final class Unreadable<T> extends Decoded<T> {
  const Unreadable(this.why);

  final BadMessage why;
}

void _inRange(int value, String name, int max) {
  if (value < 0 || value > max) {
    throw ArgumentError.value(value, name, 'is outside 0 to $max');
  }
}
