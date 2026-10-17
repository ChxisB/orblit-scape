import 'package:orblit_net/orblit_net.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

/// Lets a loopback link deliver everything queued, and what that causes.
Future<void> pump() async {
  for (var i = 0; i < 4; i++) {
    await Future<void>.delayed(Duration.zero);
  }
}

/// Waits for something that crosses a real socket, which takes turns of the
/// event loop that [pump] cannot count.
Future<void> until(
  bool Function() done, {
  Duration within = const Duration(seconds: 5),
}) async {
  final watch = Stopwatch()..start();
  while (!done()) {
    if (watch.elapsed > within) fail('Timed out waiting.');
    await Future<void>.delayed(const Duration(milliseconds: 5));
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
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      story: defaultStory,
      combat: defaultCombat,
      shops: defaultShops,
      spells: defaultSpells,
      prayers: defaultPrayers,
    );
  }

  late final ScapeServer server;
  final List<LoopbackLink> _links = [];
  SocketServer? _sockets;
  final List<GameSession> _sessions = [];

  /// Seconds on every session's clock.
  double seconds = 0;

  GameSession connect() {
    final link = LoopbackLink();
    _links.add(link);
    server.host.accept(link.host);
    final session = GameSession(
      transport: link.client,
      skills: defaultSkills,
      items: defaultItems,
      clock: () => seconds,
    );
    _sessions.add(session);
    return session;
  }

  /// A session on a real WebSocket, on this machine, read by the real clock.
  Future<GameSession> connectSocket() async {
    final sockets = _sockets ??= await server.listen();
    final session = await GameSession.connect(
      sockets.url,
      skills: defaultSkills,
      items: defaultItems,
    );
    _sessions.add(session);
    return session;
  }

  /// Ends [session] the way a player closing the window does.
  Future<void> drop(GameSession session) async {
    _sessions.remove(session);
    await session.dispose();
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
    await _sockets?.close();
    await server.dispose();
  }
}
