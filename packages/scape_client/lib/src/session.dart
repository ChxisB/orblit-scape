import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

/// A little over one tick, so the client always holds a state on each side
/// of the time it draws.
const Duration interpolationDelay = Duration(milliseconds: 700);

/// One actor as the client draws it.
typedef Actor = ({int networkId, double x, double y, int plane, bool mine});

/// The client's side of one login: what it sends, what it has been told and
/// the actors it can see. It holds no rules and draws what it is sent.
final class GameSession {
  GameSession({required Transport transport, double Function()? clock})
    : _world = World() {
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
      clock: clock,
      onMessage: _onMessage,
      onGoodbye: (reason) => refusal = reason,
    );
  }

  final World _world;
  late final ComponentType _position;
  late final NetClient _client;
  final List<String> _notices = [];

  /// The name the server knows the player by, once logged in.
  String? name;

  /// Which replicated entity is the player's own, once the server says.
  int? avatar;

  /// Why the server sent the player away, if it did.
  String? refusal;

  List<String> get notices => List.unmodifiable(_notices);

  bool get isLoggedIn => name != null;

  void login(String player) => _client.join(encodeClientMessage(Login(player)));

  void walkTo(TileCoord tile) =>
      _client.sendMessage(encodeClientMessage(Walk(tile)));

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
          _notices.add(text);
        case PrivateState():
          break;
      }
    }
  }

  Future<void> dispose() async {
    await _client.dispose();
    _world.dispose();
  }
}
