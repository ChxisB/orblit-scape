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

  Future<GameSession> join(String name) async {
    final session = game.connect()..login(name);
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
    return session;
  }

  Future<void> tickAndDraw(GameSession session, [int times = 1]) async {
    for (var i = 0; i < times; i++) {
      game.seconds += tickSeconds;
      await game.tick();
      game.seconds += interpolationDelay.inMilliseconds / 1000;
      session.advance();
    }
  }

  test(
    'a creature in view is listed with its kind and is not an actor',
    () async {
      final ana = await join('Ana');
      final guard = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.guard, tile: TileCoord(36, 32, 0)),
      );

      await tickAndDraw(ana, 2);

      expect(ana.npcs, hasLength(1));
      final sprite = ana.npcs.single;
      expect(sprite.networkId, guard.networkId);
      expect(sprite.def, NpcId.guard);
      expect((sprite.x, sprite.y, sprite.plane), (36.0, 32.0, 0));
      expect(ana.actors, hasLength(1));
      expect(ana.objects, isEmpty);
      expect(ana.groundItems, isEmpty);
    },
  );
}
