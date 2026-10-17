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

  Future<void> settle([int times = 3]) async {
    for (var i = 0; i < times; i++) {
      await pump();
      game.seconds += tickSeconds;
      await game.tick();
    }
  }

  Future<(GameSession, GameSession)> trading() async {
    final ana = await join('Ana');
    final bo = await join('Bo');
    bo.walkTo(TileCoord(33, 32, 0));
    await settle();
    game.server.items
        .container('ana', ContainerId.inventory)!
        .add(ItemId.logs, 3);
    ana.requestTrade(bo.avatar!);
    await settle();
    bo.requestTrade(ana.avatar!);
    await settle();
    return (ana, bo);
  }

  test('a trade opens for both and shows what the other offers', () async {
    final (ana, bo) = await trading();
    expect(ana.playerTrade!.partner, 'bo');
    expect(bo.playerTrade!.partner, 'ana');

    ana.offer(ItemId.logs, 2);
    await settle();

    expect(bo.playerTrade!.theirs, [ItemStack(ItemId.logs, 2)]);
    expect(ana.playerTrade!.mine, [ItemStack(ItemId.logs, 2)]);
  });

  test('declining closes the trade for both', () async {
    final (ana, bo) = await trading();

    bo.declineTrade();
    await settle();

    expect(ana.playerTrade, isNull);
    expect(bo.playerTrade, isNull);
  });

  test('a shop in its place closes the trade', () async {
    final (ana, _) = await trading();
    final keeper = game.server.npcs.spawn(
      NpcSpawn(npc: NpcId.shopkeeper, tile: TileCoord(31, 32, 0), radius: 0),
    );
    await settle();

    ana.trade(keeper.networkId);
    await settle(8);

    expect(ana.playerTrade, isNull);
    expect(ana.shop, isNotNull);
  });
}
