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
    'an object in view is listed with its state and is not an actor',
    () async {
      final ana = await join('Ana');
      final door = game.server.objects.place(
        ObjectId.door,
        TileCoord(36, 32, 0),
      );

      await tickAndDraw(ana);

      expect(ana.objects, hasLength(1));
      final sprite = ana.objects.single;
      expect(sprite.networkId, door.networkId);
      expect(sprite.def, ObjectId.door);
      expect(sprite.state, 0);
      expect((sprite.x, sprite.y, sprite.plane), (36.0, 32.0, 0));
      expect(ana.actors, hasLength(1));
      expect(ana.groundItems, isEmpty);
    },
  );

  test('a state change reaches the drawing', () async {
    final ana = await join('Ana');
    final door = game.server.objects.place(ObjectId.door, TileCoord(36, 32, 0));
    await tickAndDraw(ana);

    game.server.objects.setState(door, 1);
    await tickAndDraw(ana, 2);

    expect(ana.objects.single.state, 1);
  });

  test('picking an option sends the click and the door opens', () async {
    final ana = await join('Ana');
    final door = game.server.objects.place(ObjectId.door, TileCoord(36, 32, 0));
    await tickAndDraw(ana);

    ana.useObject(door.networkId, 0);
    await pump();
    for (var i = 0; i < 6; i++) {
      await tickAndDraw(ana);
    }

    expect(door.state, 1);
    expect(ana.objects.single.state, 1);
  });

  test('taking an item from the ground puts it in the inventory', () async {
    final ana = await join('Ana');
    final id = game.server.ground.drop(
      ItemId.logs,
      1,
      at: TileCoord(34, 32, 0),
    );
    await tickAndDraw(ana);

    ana.takeItem(id);
    await pump();
    for (var i = 0; i < 4; i++) {
      await tickAndDraw(ana);
    }

    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
    expect(ana.groundItems, isEmpty);
  });

  test('both use senders reach the server handlers', () async {
    final ana = await join('Ana');
    final door = game.server.objects.place(ObjectId.door, TileCoord(34, 32, 0));
    final used = <String>[];
    game.server.interactions
      ..addUseOnObject(ItemId.logs, ObjectId.door, (_, slot, _) {
        used.add('object $slot');
      })
      ..addUseOnItem(ItemId.logs, ItemId.coins, (_, slot, onSlot) {
        used.add('item $slot $onSlot');
      });
    game.server.items.container('ana', ContainerId.inventory)!
      ..add(ItemId.logs, 1)
      ..add(ItemId.coins, 3);
    await tickAndDraw(ana);

    ana.useItemOnItem(0, 1);
    await pump();
    await tickAndDraw(ana);
    ana.useItemOnObject(0, door.networkId);
    await pump();
    for (var i = 0; i < 4; i++) {
      await tickAndDraw(ana);
    }

    expect(used, ['item 0 1', 'object 0']);
  });
}
