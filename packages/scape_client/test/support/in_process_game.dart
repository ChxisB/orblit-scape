import 'package:orblit_net/orblit_net.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// Lets a loopback link deliver everything queued, and what that causes.
Future<void> pump() async {
  for (var i = 0; i < 4; i++) {
    await Future<void>.delayed(Duration.zero);
  }
}

/// Never waits, so the test steps the loop by hand.
final class NoPacer implements Pacer {
  @override
  Future<void> untilNext() async {}
}

TileMap openWorld() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

/// A real server and any number of sessions on loopback links, with the
/// client's clock in the test's hands.
final class InProcessGame {
  InProcessGame() {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: () => Duration.zero),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: TileCoord(32, 32, 0),
    );
  }

  late final ScapeServer server;
  final List<LoopbackLink> _links = [];
  final List<GameSession> _sessions = [];

  /// Seconds on every session's clock.
  double seconds = 0;

  GameSession connect() {
    final link = LoopbackLink();
    _links.add(link);
    server.host.accept(link.host);
    final session = GameSession(transport: link.client, clock: () => seconds);
    _sessions.add(session);
    return session;
  }

  /// Runs one server tick and delivers what it sent.
  Future<void> tick() async {
    server.loop.runTick();
    await pump();
  }

  Future<void> dispose() async {
    for (final session in _sessions) {
      await session.dispose();
    }
    for (final link in _links) {
      await link.close();
    }
    await server.dispose();
  }
}
