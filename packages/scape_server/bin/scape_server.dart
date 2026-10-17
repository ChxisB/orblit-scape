import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// Flat open ground until the starter island exists.
TileMap _openWorld() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

/// `scape_server [port] [address]`. It listens on the local machine only
/// unless an address says otherwise.
Future<void> main(List<String> args) async {
  final clock = TickClock(period: tickPeriod);
  final server = ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
    map: _openWorld(),
    spawn: TileCoord(32, 32, 0),
    skills: defaultSkills,
  );
  final sockets = await server.listen(
    port: args.isEmpty ? 7600 : int.parse(args.first),
    address: args.length > 1 ? args[1] : '127.0.0.1',
  );
  ProcessSignal.sigint.watch().listen((_) => server.loop.stop());
  stdout.writeln(
    'scape_server: ${sockets.url}, a tick every ${tickPeriod.inMilliseconds} ms',
  );
  await server.loop.run();
  await sockets.close();
  await server.dispose();
}
