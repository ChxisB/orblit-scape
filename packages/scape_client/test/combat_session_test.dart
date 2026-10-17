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

  test('a fighter and a creature in view show their hit points', () async {
    final ana = await join('Ana');
    final chicken = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: TileCoord(33, 32, 0)),
    );

    await tickAndDraw(ana, 2);

    expect(ana.healthOf(ana.avatar!), (now: 10, max: 10));
    expect(ana.healthOf(chicken.networkId), (now: 3, max: 3));
  });

  test('something that cannot fight has no hit points', () async {
    final ana = await join('Ana');
    final guard = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.guard, tile: TileCoord(33, 32, 0)),
    );

    await tickAndDraw(ana, 2);

    expect(ana.healthOf(guard.networkId), isNull);
  });

  test('an attack sends the click and the swings come back', () async {
    final ana = await join('Ana');
    final chicken = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: TileCoord(33, 32, 0)),
    );
    await tickAndDraw(ana, 2);

    ana.attack(chicken.networkId);
    await pump();
    await tickAndDraw(ana, 6);

    expect(ana.splats, isNotEmpty);
    expect(ana.splats.first.target, chicken.networkId);
    expect(ana.splats.first.damage, inInclusiveRange(0, 2));
  });
}
