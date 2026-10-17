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

  test('a cast sends the click and the flight comes back', () async {
    final ana = await join('Ana');
    game.server.items
        .container('ana', ContainerId.inventory)!
        .add(ItemId.runes, 3);
    final chicken = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.chicken, tile: TileCoord(35, 32, 0)),
    );
    await tickAndDraw(ana, 2);

    ana.cast(SpellId.strike, chicken.networkId);
    await pump();
    await tickAndDraw(ana, 3);

    expect(ana.shots, hasLength(1));
    expect(ana.shots.single.to, chicken.networkId);
    expect(ana.shots.single.from, ana.avatar);
    expect(ana.shots.single.style, CombatStyle.magic);
  });

  test('a prayer toggle sends the click and the points come back', () async {
    final ana = await join('Ana');
    await tickAndDraw(ana, 2);
    expect(ana.prayer!.active, isEmpty);

    ana.togglePrayer(PrayerId.thickSkin);
    await pump();
    await tickAndDraw(ana, 2);

    expect(ana.prayer!.points, 1);
    expect(ana.prayer!.active, [PrayerId.thickSkin]);
  });
}
