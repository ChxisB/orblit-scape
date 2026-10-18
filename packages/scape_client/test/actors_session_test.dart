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

  test('a player sees their own look and another player in view', () async {
    final ana = await join('Ana');
    final bo = await join('Bo');
    await tickAndDraw(ana, 2);
    bo.advance();

    expect(ana.lookOf(ana.avatar!), isNotNull);
    expect(ana.lookOf(bo.avatar!), bo.lookOf(bo.avatar!));
    expect(ana.animOf(bo.avatar!), Anim());
  });

  test('a creature has what it is doing but no look', () async {
    final ana = await join('Ana');
    final chicken = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: TileCoord(33, 32, 0)),
    );
    await tickAndDraw(ana, 2);

    expect(ana.animOf(chicken.networkId), isNotNull);
    expect(ana.lookOf(chicken.networkId), isNull);
  });

  test('something out of view has neither', () async {
    final ana = await join('Ana');

    expect(ana.lookOf(9999), isNull);
    expect(ana.animOf(9999), isNull);
    expect(ana.poseOf(9999), isNull);
  });

  test('a swing shows up as a pose the animator draws', () async {
    final ana = await join('Ana');
    final chicken = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: TileCoord(33, 32, 0)),
    );
    await tickAndDraw(ana, 2);

    ana.attack(chicken.networkId);
    await pump();
    var lifted = 0.0;
    for (var i = 0; i < 6; i++) {
      await tickAndDraw(ana);
      // Frames come far faster than ticks.
      final tickTime = game.seconds;
      for (var frame = 0; frame < 6; frame++) {
        game.seconds = tickTime + frame * 0.1;
        final pose = ana.poseOf(ana.avatar!)!;
        if (pose.armR < lifted) lifted = pose.armR;
      }
      game.seconds = tickTime;
    }

    expect(lifted, lessThan(-0.1));
  });
}
