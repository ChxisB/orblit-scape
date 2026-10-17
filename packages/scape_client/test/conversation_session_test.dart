import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

const double tickSeconds = 0.6;

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<void> tickAndDraw(GameSession session, [int times = 1]) async {
    for (var i = 0; i < times; i++) {
      game.seconds += tickSeconds;
      await game.tick();
      game.seconds += interpolationDelay.inMilliseconds / 1000;
      session.advance();
    }
  }

  Future<GameSession> talker() async {
    final ana = game.connect()..login('Ana');
    await pump();
    await tickAndDraw(ana);
    final brindle = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.lamplighter, tile: TileCoord(33, 32, 0)),
    );
    await tickAndDraw(ana, 2);
    ana.talk(brindle.networkId);
    await pump();
    await tickAndDraw(ana, 6);
    return ana;
  }

  test('a conversation shows a line, then choices, then ends', () async {
    final ana = await talker();
    expect(ana.line!.speaker, 'Brindle');
    expect(ana.choices, isEmpty);

    ana.continueTalk();
    await pump();
    await tickAndDraw(ana, 2);
    expect(ana.line, isNull);
    expect(ana.choices, ['I can help.', 'Not now.']);

    ana.choose(1);
    await pump();
    await tickAndDraw(ana, 2);
    expect(ana.line, isNull);
    expect(ana.choices, isEmpty);
  });

  test('walking away closes the conversation', () async {
    final ana = await talker();

    ana.walkTo(TileCoord(60, 60, 0));
    await pump();
    await tickAndDraw(ana, 12);

    expect(ana.line, isNull);
    expect(ana.choices, isEmpty);
  });
}
