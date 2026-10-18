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

  Menus menus(GameSession session) => Menus(
    session: session,
    npcs: defaultNpcs,
    objects: defaultObjects,
    items: defaultItems,
  );

  group('moving an item', () {
    Container bag(String key) =>
        game.server.items.container(key, ContainerId.inventory)!;

    test('shows in the panel once the server confirms', () async {
      final ana = await join('Ana');
      bag('ana')
        ..add(ItemId.coins, 5)
        ..add(ItemId.logs, 1);
      await game.tick();

      ana.moveItem(ContainerId.inventory, 0, ContainerId.inventory, 1);
      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 5));
      await pump();
      await game.tick();

      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
      expect(ana.itemAt(ContainerId.inventory, 1), ItemStack(ItemId.coins, 5));
    });

    test('a refused move leaves the panel as it was', () async {
      final ana = await join('Ana');
      bag('ana').add(ItemId.coins, 5);
      await game.tick();

      ana.moveItem(
        ContainerId.inventory,
        0,
        ContainerId.equipment,
        equipmentSlots.indexOf('head'),
      );
      await pump();
      await game.tick();

      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 5));
      expect(ana.itemAt(ContainerId.equipment, 0), isNull);
      expect(ana.notices.last, 'That does not go there.');
    });
  });

  group('the journal', () {
    test('is empty until a quest begins, then follows the stage', () async {
      final ana = game.connect()..login('Ana');
      await pump();
      await tickAndDraw(ana);
      expect(ana.journal, isEmpty);
      final brindle = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.lamplighter, tile: TileCoord(33, 32, 0)),
      );
      await tickAndDraw(ana, 2);

      ana.talk(brindle.networkId);
      await pump();
      await tickAndDraw(ana, 6);
      ana.continueTalk();
      await pump();
      await tickAndDraw(ana, 2);
      ana.choose(0);
      await pump();
      await tickAndDraw(ana, 4);

      expect(ana.journal.single.title, 'The Dark Lantern');
      expect(ana.journal.single.text, contains('Ask the Guard'));
      expect(ana.journal.single.done, isFalse);
    });
  });

  group('the chat box', () {
    test('keeps game, public and private lines apart', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      ana.say('hello');
      await pump();
      await tickAndDraw(bo);
      ana.whisper('Bo', 'psst');
      await pump();
      await tickAndDraw(bo);

      expect(ana.chat(ChatTab.public), ['You: hello']);
      expect(bo.chat(ChatTab.public), ['Someone: hello']);
      expect(bo.chat(ChatTab.private), ['ana: psst']);
      expect(bo.chat(ChatTab.game), isEmpty);
    });
  });

  group('the menu', () {
    test('a creature offers what its content gives it', () async {
      final ana = await join('Ana');
      final goblin = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.goblin, tile: TileCoord(40, 32, 0)),
      );
      final shopkeeper = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.shopkeeper, tile: TileCoord(34, 32, 0)),
      );
      await tickAndDraw(ana, 2);

      final labels = {
        for (final sprite in ana.npcs)
          sprite.networkId: [
            for (final entry in menus(ana).onNpc(sprite)) entry.label,
          ],
      };

      expect(labels[goblin.networkId], ['Attack Goblin']);
      expect(labels[shopkeeper.networkId], [
        'Trade Shopkeeper',
        'Talk to Shopkeeper',
      ]);
    });

    test('an object offers the option for the state it is in', () async {
      final ana = await join('Ana');
      final door = game.server.objects.place(
        ObjectId.door,
        TileCoord(34, 32, 0),
      );
      await tickAndDraw(ana, 2);
      expect(menus(ana).onObject(ana.objects.single).map((e) => e.label), [
        'Open Door',
      ]);

      game.server.objects.setState(door, 1);
      await tickAndDraw(ana, 2);

      expect(menus(ana).onObject(ana.objects.single).map((e) => e.label), [
        'Close Door',
      ]);
    });

    test('an entry sends the click it names', () async {
      final ana = await join('Ana');
      final id = game.server.ground.drop(
        ItemId.logs,
        1,
        at: TileCoord(33, 32, 0),
      );
      await tickAndDraw(ana, 2);
      final take = menus(ana).onGround(ana.groundItems.single).single;

      take.run();
      await pump();
      await tickAndDraw(ana, 3);

      expect(take.label, 'Take Logs');
      expect(ana.groundItems.map((g) => g.networkId), isNot(contains(id)));
      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
    });

    test('another player offers a trade, you do not', () async {
      final ana = await join('Ana');
      await join('Bo');
      await tickAndDraw(ana, 2);

      final entries = {
        for (final actor in ana.actors)
          actor.mine: menus(ana).onPlayer(actor).length,
      };

      expect(entries, {true: 0, false: 1});
    });
  });

  group('the minimap', () {
    test('shows others near the player, and not the player', () async {
      final ana = await join('Ana');
      game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.guard, tile: TileCoord(35, 34, 0)),
      );
      game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.guard, tile: TileCoord(60, 60, 0)),
      );
      await tickAndDraw(ana, 2);

      final dots = minimapDots(ana);

      expect(dots, hasLength(1));
      expect(dots.single.kind, DotKind.npc);
      expect((dots.single.dx, dots.single.dy), (3.0, 2.0));
    });

    test('leaves out a creature on another plane', () async {
      final ana = await join('Ana');
      game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.guard, tile: TileCoord(35, 34, 1)),
      );
      await tickAndDraw(ana, 2);

      expect(ana.npcs, hasLength(1));
      expect(minimapDots(ana), isEmpty);
    });

    test('is empty before the player is in the world', () {
      expect(minimapDots(game.connect()), isEmpty);
    });
  });
}
