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
    position = world.registerComponent(
      tilePositionName,
      kind: ComponentKind.float32,
      arity: tilePositionFloats,
    );
    ground = world.registerComponent(
      groundItemName,
      kind: ComponentKind.int32,
      arity: groundItemInts,
    );
    object = world.registerComponent(
      worldObjectName,
      kind: ComponentKind.int32,
      arity: worldObjectInts,
    );
    npc = world.registerComponent(
      npcName,
      kind: ComponentKind.int32,
      arity: npcInts,
    );
    health = world.registerComponent(
      healthName,
      kind: ComponentKind.int32,
      arity: healthInts,
    );
    client = NetClient(
      world: world,
      set: ReplicationSet([position, ground, object, npc, health]),
      networkId: networkId,
      transport: link.client,
      onMessage: (body) => received.add(decodeServerMessage(body)),
      onGoodbye: goodbyes.add,
    );
    server.host.accept(link.host);
  }

  final World world = World();
  final LoopbackLink link = LoopbackLink();
  late final ComponentType position;
  late final ComponentType ground;
  late final ComponentType object;
  late final ComponentType npc;
  late final ComponentType health;
  late final NetClient client;
  final List<Decoded<ServerMessage>> received = [];
  final List<String> goodbyes = [];

  /// The messages that were understood.
  List<ServerMessage> get messages => [
    for (final decoded in received)
      if (decoded case Understood(:final message)) message,
  ];

  /// The items on the ground this client has been told about, as
  /// (item, count) by network id.
  Map<int, (int, int)> get groundItems => {
    for (final networkId in client.networkIds)
      if (client.world.has(client.entityFor(networkId)!, ground))
        networkId: _readGround(client.entityFor(networkId)!),
  };

  (int, int) _readGround(int entity) {
    final data = ByteData.sublistView(world.bytesOf(entity, ground)!);
    return (data.getInt32(0, Endian.little), data.getInt32(4, Endian.little));
  }

  /// The objects this client has been told about, as (definition, state)
  /// by network id.
  Map<int, (int, int)> get objects => {
    for (final networkId in client.networkIds)
      if (client.world.has(client.entityFor(networkId)!, object))
        networkId: _readObject(client.entityFor(networkId)!),
  };

  (int, int) _readObject(int entity) {
    final data = ByteData.sublistView(world.bytesOf(entity, object)!);
    return (data.getInt32(0, Endian.little), data.getInt32(4, Endian.little));
  }

  /// The creatures this client has been told about, as their definition id
  /// by network id.
  Map<int, int> get npcs => {
    for (final networkId in client.networkIds)
      if (client.world.has(client.entityFor(networkId)!, npc))
        networkId: ByteData.sublistView(
          world.bytesOf(client.entityFor(networkId)!, npc)!,
        ).getInt32(0, Endian.little),
  };

  /// The hit points of the entity with [networkId] as this client sees them,
  /// or null when it has none.
  ({int now, int max})? healthOf(int networkId) {
    final entity = client.entityFor(networkId);
    final bytes = entity == null ? null : world.bytesOf(entity, health);
    if (bytes == null) return null;
    final data = ByteData.sublistView(bytes);
    return (
      now: data.getInt32(0, Endian.little),
      max: data.getInt32(4, Endian.little),
    );
  }

  /// The tile the entity with [networkId] stands on, as this client sees it.
  TileCoord? tileOf(int networkId) {
    final entity = client.entityFor(networkId);
    if (entity == null) return null;
    return tileNearest(readTilePoint(world.bytesOf(entity, position)!));
  }

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
