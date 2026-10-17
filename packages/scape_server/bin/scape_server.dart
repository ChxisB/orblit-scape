import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_server/scape_server.dart';

Future<void> main() async {
  final clock = TickClock(period: tickPeriod);
  final server = ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
  );
  ProcessSignal.sigint.watch().listen((_) => server.loop.stop());
  stdout.writeln('scape_server: a tick every ${tickPeriod.inMilliseconds} ms');
  await server.loop.run();
  await server.dispose();
}
