import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

const double tickSeconds = 0.6;

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<GameSession> join(String name) async {
    final session = game.connect()..login(name);
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
    return session;
  }

  /// Drawing runs behind the newest state by the interpolation delay, so the
  /// clock has to pass it before the newest state shows.
  void catchUp(GameSession session) {
    game.seconds += interpolationDelay.inMilliseconds / 1000;
    session.advance();
  }

  double drawnX(GameSession session) {
    session.advance();
    return session.actors.singleWhere((a) => a.mine).x;
  }

  test('a login gives a name, an avatar and an actor that is mine', () async {
    final ana = await join('Ana');

    expect(ana.isLoggedIn, isTrue);
    expect(ana.name, 'ana');
    expect(ana.avatar, isNotNull);
    catchUp(ana);
    expect(ana.actors.single.mine, isTrue);
    expect(ana.actors.single.networkId, ana.avatar);
  });

  test('a name already in use is refused with the reason', () async {
    await join('Ana');
    final second = game.connect()..login('ANA');
    await pump();

    expect(second.isLoggedIn, isFalse);
    expect(second.refusal, LoginRefusal.nameTaken.name);
  });

  test('another player is an actor that is not mine', () async {
    final ana = await join('Ana');
    await join('Bo');
    game.seconds += tickSeconds;
    await game.tick();
    catchUp(ana);

    expect(ana.actors, hasLength(2));
    expect(ana.actors.where((a) => a.mine), hasLength(1));
  });

  test('a walk moves the actor smoothly across each tick', () async {
    final ana = await join('Ana');
    ana.walkTo(TileCoord(36, 32, 0));
    await pump();

    final seen = <double>[];
    for (var i = 0; i < 5; i++) {
      for (var frame = 0; frame < 6; frame++) {
        seen.add(drawnX(ana));
        game.seconds += tickSeconds / 6;
      }
      await game.tick();
    }

    for (var i = 1; i < seen.length; i++) {
      expect(seen[i] - seen[i - 1], lessThanOrEqualTo(1 / 6 + 1e-3));
      expect(seen[i], greaterThanOrEqualTo(seen[i - 1] - 1e-6));
    }
    expect(seen.any((x) => x != x.roundToDouble()), isTrue);
  });

  test('a walk ends on the tile that was clicked', () async {
    final ana = await join('Ana');
    ana.walkTo(TileCoord(35, 32, 0));
    await pump();

    for (var i = 0; i < 6; i++) {
      game.seconds += tickSeconds;
      await game.tick();
    }
    catchUp(ana);

    expect(ana.actors.single.x, 35);
    expect(ana.actors.single.y, 32);
  });
}
