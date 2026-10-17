import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

/// These run on the real clock, so the blend delay has to pass for real.
const Duration settle = Duration(milliseconds: 900);

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<GameSession> join(String name) async {
    final session = await game.connectSocket();
    session.login(name);
    await until(() => session.avatar != null);
    return session;
  }

  Future<void> ticks(int count) async {
    for (var i = 0; i < count; i++) {
      game.server.loop.runTick();
      await Future<void>.delayed(const Duration(milliseconds: 40));
    }
  }

  List<Actor> seenBy(GameSession session) {
    session.advance();
    return session.actors;
  }

  test('two clients on sockets see each other walk', () async {
    final ana = await join('Ana');
    final bo = await join('Bo');
    await ticks(2);

    ana.walkTo(TileCoord(36, 32, 0));
    await Future<void>.delayed(const Duration(milliseconds: 50));
    await ticks(6);
    await Future<void>.delayed(settle);

    final theirs = seenBy(bo).singleWhere((a) => !a.mine);
    expect(theirs.networkId, ana.avatar);
    expect((theirs.x, theirs.y), (36, 32));
    expect(seenBy(ana).singleWhere((a) => !a.mine).networkId, bo.avatar);
  });

  test('a player who closes the window leaves the other\'s view', () async {
    final ana = await join('Ana');
    final bo = await join('Bo');
    await ticks(2);
    await Future<void>.delayed(settle);
    expect(seenBy(bo), hasLength(2));

    await game.drop(ana);
    await Future<void>.delayed(const Duration(milliseconds: 50));
    await ticks(12);
    await Future<void>.delayed(settle);

    expect(seenBy(bo), hasLength(1));
  });
}
