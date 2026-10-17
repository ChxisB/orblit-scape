import 'dart:typed_data';

import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<GameSession> join(String name) async {
    final session = game.connect()..login(name);
    await pump();
    await game.tick();
    return session;
  }

  test('a new session holds no experience and is level 1', () async {
    final ana = await join('Ana');

    expect(ana.xpOf('mining'), 0);
    expect(ana.levelOf('mining'), 1);
  });

  test(
    'experience the server grants reaches the owner and no one else',
    () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final enough = defaultSkills.curve.xpForLevel(5) + 3;

      game.server.skills.grant('ana', 'mining', enough);
      await game.tick();

      expect(ana.xpOf('mining'), enough);
      expect(ana.levelOf('mining'), 5);
      expect(ana.xpOf('fishing'), 0);
      expect(bo.xpOf('mining'), 0);
    },
  );

  test('later grants add to what the client holds', () async {
    final ana = await join('Ana');

    game.server.skills.grant('ana', 'fishing', 40);
    await game.tick();
    game.server.skills.grant('ana', 'mining', 70);
    game.server.skills.grant('ana', 'fishing', 2);
    await game.tick();

    expect(ana.xpOf('fishing'), 42);
    expect(ana.xpOf('mining'), 70);
  });

  test('a skill the game does not have is refused', () async {
    final ana = await join('Ana');

    expect(() => ana.xpOf('dancing'), throwsArgumentError);
    expect(() => ana.levelOf('dancing'), throwsArgumentError);
  });

  test('private bytes of another kind or of no shape change nothing', () async {
    final ana = await join('Ana');
    game.server.skills.grant('ana', 'fishing', 40);
    await game.tick();

    for (final bytes in [
      Uint8List(0),
      Uint8List.fromList([skillsDeltaTag + 1, 0, 0]),
      Uint8List.fromList([skillsDeltaTag, 9]),
    ]) {
      game.server.host.sendMessage(
        'ana',
        encodeServerMessage(PrivateState(bytes)),
      );
    }
    await game.tick();

    expect(ana.xpOf('fishing'), 40);
  });
}
