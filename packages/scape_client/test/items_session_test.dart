import 'dart:typed_data';

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

  /// Ticks the server, then lets drawing reach the newest state.
  Future<void> tickAndDraw(GameSession session) async {
    game.seconds += tickSeconds;
    await game.tick();
    game.seconds += interpolationDelay.inMilliseconds / 1000;
    session.advance();
  }

  Container bag(String key, [ContainerId id = ContainerId.inventory]) =>
      game.server.items.container(key, id)!;

  test('a new session holds nothing', () async {
    final ana = await join('Ana');

    for (final id in ContainerId.values) {
      expect(ana.itemAt(id, 0), isNull);
    }
    expect(ana.groundItems, isEmpty);
  });

  test('a stack the server adds appears in the same slot', () async {
    final ana = await join('Ana');

    bag('ana').add(ItemId.coins, 25);
    await game.tick();

    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 25));
    expect(ana.itemAt(ContainerId.bank, 0), isNull);
  });

  test('a stack that is taken from shrinks, then empties', () async {
    final ana = await join('Ana');
    bag('ana').add(ItemId.coins, 5);
    await game.tick();

    bag('ana').take(0, 2);
    await game.tick();
    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 3));

    bag('ana').take(0);
    await game.tick();
    expect(ana.itemAt(ContainerId.inventory, 0), isNull);
  });

  test('the bank, sent in pieces, arrives whole', () async {
    final ana = await join('Ana');
    final bank = bag('ana', ContainerId.bank);
    for (var slot = 0; slot < bankTabs * bankTabSize; slot++) {
      bank.add(ItemId.logs, 1);
    }

    await game.tick();

    for (final slot in [0, 99, 100, 511, 512, 799]) {
      expect(ana.itemAt(ContainerId.bank, slot), ItemStack(ItemId.logs, 1));
    }
  });

  test('the server swapping two slots is mirrored', () async {
    final ana = await join('Ana');
    bag('ana')
      ..add(ItemId.coins, 5)
      ..add(ItemId.logs, 1);
    await game.tick();

    bag('ana').swap(0, 1);
    await game.tick();

    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
    expect(ana.itemAt(ContainerId.inventory, 1), ItemStack(ItemId.coins, 5));
  });

  test('another player never sees these containers', () async {
    await join('Ana');
    final bo = await join('Bo');

    bag('ana').add(ItemId.coins, 25);
    await game.tick();

    expect(bo.itemAt(ContainerId.inventory, 0), isNull);
  });

  test('ground items are drawn apart from the actors', () async {
    final ana = await join('Ana');
    final id = game.server.ground.drop(
      ItemId.logs,
      3,
      at: TileCoord(33, 32, 0),
    );

    await tickAndDraw(ana);

    expect(ana.groundItems, hasLength(1));
    final sprite = ana.groundItems.single;
    expect(sprite.networkId, id);
    expect(sprite.item, ItemId.logs);
    expect(sprite.count, 3);
    expect(ana.actors.map((a) => a.networkId), isNot(contains(id)));
  });

  test('an item picked up leaves the ground', () async {
    final ana = await join('Ana');
    final id = game.server.ground.drop(
      ItemId.logs,
      1,
      at: TileCoord(32, 32, 0),
    );
    await tickAndDraw(ana);
    expect(ana.groundItems, hasLength(1));

    game.server.ground.pickUp('ana', id);
    await tickAndDraw(ana);

    expect(ana.groundItems, isEmpty);
    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.logs, 1));
  });

  test('private bytes that are not container slots change nothing', () async {
    final ana = await join('Ana');
    bag('ana').add(ItemId.coins, 5);
    await game.tick();

    for (final bytes in [
      Uint8List.fromList([itemsDeltaTag, 9]),
      Uint8List.fromList([itemsDeltaTag, 0, 1]),
      Uint8List.fromList([itemsDeltaTag, 0, 0, 0, 99, 0, 1, 0, 0, 0]),
    ]) {
      game.server.host.sendMessage(
        'ana',
        encodeServerMessage(PrivateState(bytes)),
      );
    }
    await game.tick();

    expect(ana.itemAt(ContainerId.inventory, 0), ItemStack(ItemId.coins, 5));
  });
}
