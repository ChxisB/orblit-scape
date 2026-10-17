import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
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

Future<void> main() async {
  final clock = TickClock(period: tickPeriod);
  final server = ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
    map: _openWorld(),
    spawn: TileCoord(32, 32, 0),
  );
  ProcessSignal.sigint.watch().listen((_) => server.loop.stop());
  stdout.writeln('scape_server: a tick every ${tickPeriod.inMilliseconds} ms');
  await server.loop.run();
  await server.dispose();
}
