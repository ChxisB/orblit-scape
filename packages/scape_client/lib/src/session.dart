import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

/// A little over one tick, so the client always holds a state on each side
/// of the time it draws.
const Duration interpolationDelay = Duration(milliseconds: 700);

/// How long a line of chat stays over a head.
const double bubbleSeconds = 5;

/// The most notices and whispers kept. The oldest go first.
const int _keepLines = 100;

/// One actor as the client draws it.
typedef Actor = ({int networkId, double x, double y, int plane, bool mine});

/// A line of public chat to draw over the actor [networkId] is.
typedef Bubble = ({int networkId, String text});

/// A private message from the player called [from].
typedef Heard = ({String from, String text});

/// The client's side of one login: what it sends, what it has been told and
/// the actors it can see. It holds no rules and draws what it is sent.
final class GameSession {
  GameSession({required Transport transport, double Function()? clock})
    : _transport = transport,
      _now = clock ?? _stopwatchClock(),
      _world = World() {
    final networkId = _world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    _position = _world.registerComponent(
      tilePositionName,
      kind: ComponentKind.float32,
      arity: tilePositionFloats,
    );
    _client = NetClient(
      world: _world,
      set: ReplicationSet([_position]),
      networkId: networkId,
      transport: transport,
      interpolationDelay: interpolationDelay,
      clock: _now,
      onMessage: _onMessage,
      onGoodbye: (reason) => refusal = reason,
    );
  }

  /// Opens a connection to the server at [url]. The session closes it on
  /// [dispose].
  static Future<GameSession> connect(
    Uri url, {
    double Function()? clock,
  }) async =>
      GameSession(transport: await SocketTransport.connect(url), clock: clock);

  static double Function() _stopwatchClock() {
    final stopwatch = Stopwatch()..start();
    return () => stopwatch.elapsedMicroseconds / 1e6;
  }

  final Transport _transport;
  final double Function() _now;
  final World _world;
  late final ComponentType _position;
  late final NetClient _client;
  final List<String> _notices = [];
  final List<Heard> _whispers = [];
  final Map<int, ({String text, double until})> _bubbles = {};

  /// The name the server knows the player by, once logged in.
  String? name;

  /// Which replicated entity is the player's own, once the server says.
  int? avatar;

  /// Why the server sent the player away, if it did.
  String? refusal;

  /// Friends, with whether each is logged in. Empty until the server sends it.
  List<Friend> friends = const [];

  /// Names the player ignores.
  List<String> ignored = const [];

  List<String> get notices => List.unmodifiable(_notices);

  /// Private messages received, oldest first.
  List<Heard> get whispers => List.unmodifiable(_whispers);

  /// The chat lines to draw now, one per speaker, the newest of each. A speaker
  /// is one entry for as long as the session lasts, which is small.
  List<Bubble> get bubbles => [
    for (final MapEntry(:key, :value) in _bubbles.entries)
      if (value.until > _now()) (networkId: key, text: value.text),
  ];

  bool get isLoggedIn => name != null;

  void login(String player) => _client.join(encodeClientMessage(Login(player)));

  void walkTo(TileCoord tile) =>
      _client.sendMessage(encodeClientMessage(Walk(tile)));

  /// Says [text] to the players nearby. The text must satisfy [isValidText].
  void say(String text) => _client.sendMessage(encodeClientMessage(Chat(text)));

  /// Sends [text] privately to the player called [to]. Both must be valid.
  void whisper(String to, String text) =>
      _client.sendMessage(encodeClientMessage(Whisper(to: to, text: text)));

  /// Puts [name] on [list], or takes it off. The name must satisfy
  /// [isValidName].
  void editList(ListKind list, String name, {required bool add}) =>
      _client.sendMessage(
        encodeClientMessage(EditList(list: list, add: add, name: name)),
      );

  /// Blends the world towards where the server was a moment ago. Call once a
  /// frame.
  void advance() => _client.advance();

  /// Every actor in view, with the position it is drawn at now.
  List<Actor> get actors => [
    for (final networkId in _client.networkIds) _actorFor(networkId),
  ];

  Actor _actorFor(int networkId) {
    final bytes = _world.bytesOf(_client.entityFor(networkId)!, _position)!;
    final point = readTilePoint(bytes);
    return (
      networkId: networkId,
      x: point.x,
      y: point.y,
      plane: point.plane,
      mine: networkId == avatar,
    );
  }

  void _onMessage(Uint8List body) {
    if (decodeServerMessage(body) case Understood(:final message)) {
      switch (message) {
        case LoginOk(:final name):
          this.name = name;
        case Avatar(:final networkId):
          avatar = networkId;
        case Notice(:final text):
          _keep(_notices, text);
        case Said(:final speaker, :final text):
          _bubbles[speaker] = (text: text, until: _now() + bubbleSeconds);
        case Whispered(:final from, :final text):
          _keep(_whispers, (from: from, text: text));
        case SocialLists(:final friends, :final ignored):
          this.friends = friends;
          this.ignored = ignored;
        case PrivateState():
          break;
      }
    }
  }

  void _keep<T>(List<T> lines, T line) {
    lines.add(line);
    if (lines.length > _keepLines) lines.removeAt(0);
  }

  Future<void> dispose() async {
    await _client.dispose();
    await _transport.close();
    _world.dispose();
  }
}
