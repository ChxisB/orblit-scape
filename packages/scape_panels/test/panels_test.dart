import 'package:flutter/widgets.dart' hide Container;
import 'package:flutter_test/flutter_test.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_panels/scape_panels.dart';

import 'support/in_process_game.dart';

const double tickSeconds = 0.6;

/// The width the layout tests draw at, set by each test before it draws.
double _width = 1280;

void main() {
  late InProcessGame game;
  late GameSession ana;
  late PanelController controller;

  setUp(() async {
    game = InProcessGame();
    ana = game.connect()..login('Ana');
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
    controller = PanelController(
      session: ana,
      menus: Menus(
        session: ana,
        npcs: defaultNpcs,
        objects: defaultObjects,
        items: defaultItems,
      ),
    );
  });
  tearDown(() => game.dispose());

  Container bag() => game.server.items.container('ana', ContainerId.inventory)!;

  /// Lets the server tick, and the panels draw what it sent.
  Future<void> settle(WidgetTester tester, [int ticks = 1]) async {
    await tester.runAsync(() async {
      for (var i = 0; i < ticks; i++) {
        await pump();
        game.seconds += tickSeconds;
        await game.tick();
        game.seconds += interpolationDelay.inMilliseconds / 1000;
        ana.advance();
      }
    });
    controller.refresh();
    await tester.pump();
  }

  Future<void> show(WidgetTester tester, {double width = 1280}) async {
    tester.view
      ..physicalSize = Size(width, 740)
      ..devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: MediaQuery(
          data: MediaQueryData(size: Size(width, 740)),
          child: ScapeShell(controller: controller, skills: defaultSkills),
        ),
      ),
    );
  }

  group('the state', () {
    test('refuses a volume or view distance out of range', () {
      expect(() => PanelState(volume: maxVolume + 1), throwsArgumentError);
      expect(() => PanelState(viewDistance: 1), throwsArgumentError);
    });

    test('puts a picked stack down when asked for null', () {
      final held = PanelState(
        picked: (container: ContainerId.inventory, slot: 2),
      );
      expect(held.copyWith(tab: SideTab.skills).picked, isNotNull);
      expect(held.copyWith(picked: () => null).picked, isNull);
    });

    test('the settings stop at their ends', () {
      for (var i = 0; i < 30; i++) {
        controller
          ..handle('volume:1')
          ..handle('view:-1');
      }
      expect(controller.state.volume, maxVolume);
      expect(controller.state.viewDistance, minViewDistance);
    });
  });

  group('the inventory', () {
    testWidgets('shows what the server holds', (tester) async {
      bag()
        ..add(ItemId.coins, 5)
        ..add(ItemId.logs, 1);
      await show(tester);
      await settle(tester);

      expect(find.text('Coins x5'), findsOneWidget);
      expect(find.text('Logs'), findsOneWidget);
    });

    testWidgets('a drag moves the stack once the server confirms', (
      tester,
    ) async {
      bag()
        ..add(ItemId.coins, 5)
        ..add(ItemId.logs, 1);
      await show(tester);
      await settle(tester);
      double left(String text) => tester.getTopLeft(find.text(text)).dx;
      expect(left('Coins x5'), lessThan(left('Logs')));

      await tester.tap(find.text('Coins x5'));
      await tester.pump();
      await tester.tap(find.text('Logs'));
      await tester.pump();
      expect(left('Coins x5'), lessThan(left('Logs')));
      await settle(tester);

      expect(left('Logs'), lessThan(left('Coins x5')));
    });

    testWidgets('a refused drag leaves the panel as it was', (tester) async {
      bag().add(ItemId.coins, 5);
      await show(tester);
      await settle(tester);

      await tester.tap(find.text('Coins x5'));
      await tester.pump();
      await tester.tap(find.text('equ'));
      await tester.pump();
      await tester.tap(find.byType(GestureDetector).at(7));
      await settle(tester, 2);
      await tester.tap(find.text('inv'));
      await tester.pump();

      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 5));
      expect(ana.notices.last, 'That does not go there.');
      expect(find.text('Coins x5'), findsOneWidget);
    });
  });

  group('the side tabs', () {
    testWidgets('skills lists each skill with its level', (tester) async {
      await show(tester);
      await tester.tap(find.text('ski'));
      await tester.pump();

      expect(find.text(defaultSkills.skills.first.name), findsOneWidget);
      expect(find.text('No quests begun.'), findsNothing);
    });

    testWidgets('quests says so when none has begun', (tester) async {
      await show(tester);
      await tester.tap(find.text('que'));
      await tester.pump();

      expect(find.text('No quests begun.'), findsOneWidget);
    });
  });

  group('a conversation', () {
    testWidgets('shows in the side panel, and a choice begins a quest', (
      tester,
    ) async {
      final brindle = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.lamplighter, tile: TileCoord(33, 32, 0)),
      );
      await show(tester);
      await settle(tester, 2);

      ana.talk(brindle.networkId);
      await settle(tester, 6);
      expect(find.text('Brindle'), findsOneWidget);
      await tester.tap(find.text('Continue'));
      await settle(tester, 3);
      await tester.tap(find.text('I can help.'));
      await settle(tester, 4);
      await tester.tap(find.text('Continue'));
      await settle(tester, 2);
      await tester.tap(find.text('que'));
      await tester.pump();

      expect(find.text('The Dark Lantern'), findsOneWidget);
    });
  });

  group('the windows', () {
    testWidgets('a shop line buys one', (tester) async {
      bag().add(ItemId.coins, 100);
      final keeper = game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.shopkeeper, tile: TileCoord(33, 32, 0), radius: 0),
      );
      await show(tester);
      await settle(tester, 2);
      ana.trade(keeper.networkId);
      await settle(tester, 6);

      await tester.tap(find.textContaining('Bronze axe'));
      await settle(tester, 3);

      expect(bag().countOf(ItemId.bronzeAxe), 1);
      expect(find.textContaining('Bronze axe (4 left)'), findsOneWidget);
    });

    testWidgets('an open bank takes a tap on the inventory and gives it back', (
      tester,
    ) async {
      bag().add(ItemId.feathers, 8);
      final booth = game.server.objects.place(
        ObjectId.bankBooth,
        TileCoord(33, 32, 0),
      );
      await show(tester);
      await settle(tester, 2);
      ana.useObject(booth.networkId, 0);
      await settle(tester, 6);
      expect(find.text('Bank'), findsOneWidget);

      await tester.tap(find.text('Feathers x8'));
      await settle(tester, 3);
      expect(ana.itemAt(ContainerId.bank, 0), ItemStack(ItemId.feathers, 1));

      await tester.tap(find.text('Feathers'));
      await settle(tester, 3);
      expect(ana.itemAt(ContainerId.bank, 0), isNull);
      expect(bag().countOf(ItemId.feathers), 8);
    });

    testWidgets('an open trade offers one more of a tapped stack', (
      tester,
    ) async {
      final bo = game.connect()..login('Bo');
      await settle(tester);
      bo.walkTo(TileCoord(33, 32, 0));
      await settle(tester, 3);
      bag().add(ItemId.logs, 3);
      ana.requestTrade(bo.avatar!);
      await settle(tester, 3);
      bo.requestTrade(ana.avatar!);
      await settle(tester, 3);
      await show(tester);
      await settle(tester);
      expect(find.text('Trading with bo'), findsOneWidget);

      await tester.tap(find.text('Logs').first);
      await settle(tester, 3);
      await tester.tap(find.text('Logs').first);
      await settle(tester, 3);

      expect(ana.playerTrade!.mine, [ItemStack(ItemId.logs, 2)]);
    });
  });

  group('a long bank', () {
    testWidgets('scrolls and keeps the inventory in reach on a phone', (
      tester,
    ) async {
      final vault = game.server.items.container('ana', ContainerId.bank)!;
      vault.add(ItemId.bronzeAxe, 40, tab: 0);
      final booth = game.server.objects.place(
        ObjectId.bankBooth,
        TileCoord(33, 32, 0),
      );
      await show(tester, width: 360);
      await settle(tester, 2);
      ana.useObject(booth.networkId, 0);
      await settle(tester, 6);

      expect(find.byType(Scrollable), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  });

  group('a tap', () {
    testWidgets('does not redraw for the same hover text', (tester) async {
      var draws = 0;
      controller.addListener(() => draws++);

      controller
        ..hover('Take Logs')
        ..hover('Take Logs');

      expect(draws, 1);
    });

    testWidgets('puts a picked stack down when the server empties its slot', (
      tester,
    ) async {
      bag().add(ItemId.logs, 1);
      await show(tester);
      await settle(tester, 2);
      await tester.tap(find.text('Logs'));
      await tester.pump();
      expect(controller.state.picked, isNotNull);

      bag().take(0);
      await settle(tester);

      expect(controller.state.picked, isNull);
    });
  });

  group('the layout', () {
    Future<void> populate(WidgetTester tester) async {
      bag()
        ..add(ItemId.coins, 25)
        ..add(ItemId.logs, 1)
        ..add(ItemId.bronzeAxe, 1);
      game.server.npcs.spawn(
        NpcSpawn(npc: NpcId.guard, tile: TileCoord(36, 34, 0)),
      );
      await show(tester, width: _width);
      await settle(tester, 2);
      ana.say('hello');
      await settle(tester);
      controller
        ..hover('Talk to Guard')
        ..handle('chat:public')
        ..handle('slot:inventory:1');
      await tester.pump();
    }

    for (final width in [360.0, 1280.0]) {
      testWidgets('matches its golden at ${width.round()} wide', (
        tester,
      ) async {
        _width = width;
        await populate(tester);

        await expectLater(
          find.byType(ScapeShell),
          matchesGoldenFile('goldens/shell_${width.round()}.png'),
        );
      });
    }
  });

  group('the menu', () {
    testWidgets('an entry runs once, and the menu goes', (tester) async {
      game.server.ground.drop(ItemId.logs, 1, at: TileCoord(33, 32, 0));
      await show(tester);
      await settle(tester, 2);

      controller.openMenu(controller.menus.onGround(ana.groundItems.single));
      await tester.pump();
      await tester.tap(find.text('Take Logs'));
      await settle(tester, 3);

      expect(controller.state.menu, isEmpty);
      expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
      expect(find.text('Cancel'), findsNothing);
    });
  });
}
