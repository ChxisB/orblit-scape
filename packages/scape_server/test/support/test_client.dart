import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// Lets a loopback link deliver everything queued, and what that causes.
Future<void> pump() async {
  for (var i = 0; i < 4; i++) {
    await Future<void>.delayed(Duration.zero);
  }
}

/// A client on a loopback link to [server], reading what it is sent.
final class TestClient {
  TestClient(ScapeServer server) {
    final networkId = world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    client = NetClient(
      world: world,
      set: ReplicationSet([]),
      networkId: networkId,
      transport: link.client,
      onMessage: (body) => received.add(decodeServerMessage(body)),
      onGoodbye: goodbyes.add,
    );
    server.host.accept(link.host);
  }

  final World world = World();
  final LoopbackLink link = LoopbackLink();
  late final NetClient client;
  final List<Decoded<ServerMessage>> received = [];
  final List<String> goodbyes = [];

  /// The messages that were understood.
  List<ServerMessage> get messages => [
    for (final decoded in received)
      if (decoded case Understood(:final message)) message,
  ];

  Future<void> login(String name) async {
    client.join(encodeClientMessage(Login(name)));
    await pump();
  }

  Future<void> send(ClientMessage message) async {
    client.sendMessage(encodeClientMessage(message));
    await pump();
  }

  Future<void> sendBytes(Uint8List bytes) async {
    client.sendMessage(bytes);
    await pump();
  }

  Future<void> dispose() async {
    await link.close();
    world.dispose();
  }
}
