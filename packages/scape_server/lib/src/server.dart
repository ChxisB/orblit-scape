import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/summary_text.dart';
import 'engine/tick_loop.dart';
import 'features.dart';
import 'inbox.dart';
import 'wire.dart';

/// The bytes the net layer adds round a message before the host counts it.
const int _wrapperBytes = 4;

/// Ticks a connection may stay silent before it must log in, 12 seconds.
const int _loginTicks = 20;

final class _HostLink implements WireLink {
  late final NetHost host;

  @override
  bool isLive(String key) => host.sessionState(key) == SessionState.live;

  @override
  void send(String key, Uint8List body) => host.sendMessage(key, body);
}

/// The world, the host and the loop, wired together.
final class ScapeServer {
  ScapeServer({
    required TickClock clock,
    required Pacer pacer,
    required void Function(String line) log,
  }) {
    final networkId = world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    final link = _HostLink();
    wire = Wire(link: link, inbox: inbox);
    host = NetHost(
      world: world,
      set: ReplicationSet([]),
      networkId: networkId,
      clock: clock,
      onOverrun: (summary) => log(describeLateTick(summary)),
      sessions: SessionPolicy(
        admit: wire.admit,
        onJoin: wire.onJoin,
        onGone: wire.onGone,
        joinTicks: _loginTicks,
      ),
      budget: InboundBudget(
        messagesPerTick: 20,
        bytesPerMessage: clientMessageLimit + _wrapperBytes,
        bytesPerTick: 4096,
      ),
      onMessage: wire.onMessage,
    );
    link.host = host;
    registerFeatures(registry);
    loop = TickLoop(host: host, registry: registry, pacer: pacer);
  }

  final World world = World();
  final Registry registry = Registry();
  final Inbox inbox = Inbox();
  late final Wire wire;
  late final NetHost host;
  late final TickLoop loop;

  Future<void> dispose() async {
    await host.dispose();
    world.dispose();
  }
}
