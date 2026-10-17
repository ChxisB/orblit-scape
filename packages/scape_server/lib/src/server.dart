import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';

import 'engine/registry.dart';
import 'engine/summary_text.dart';
import 'engine/tick_loop.dart';
import 'features.dart';

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
    host = NetHost(
      world: world,
      set: ReplicationSet([]),
      networkId: networkId,
      clock: clock,
      onOverrun: (summary) => log(describeLateTick(summary)),
    );
    registerFeatures(registry);
    loop = TickLoop(host: host, registry: registry, pacer: pacer);
  }

  final World world = World();
  final Registry registry = Registry();
  late final NetHost host;
  late final TickLoop loop;

  Future<void> dispose() async {
    await host.dispose();
    world.dispose();
  }
}
