import 'dart:typed_data';

import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:scape_server/src/persistence/keys.dart';
import 'package:scape_server/src/persistence/trade_journal.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/storage.dart';
import 'support/test_client.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  List<TradeView> get views => messages.whereType<TradeView>().toList();

  int get closes => messages.whereType<WindowClosed>().length;

  Future<void> click(int networkId) =>
      send(Interact(kind: InteractKind.player, id: networkId, option: 0));

  Future<void> offer(int item, int count) =>
      send(TradeOffer(item: item, count: count));
}

/// A store that stops keeping writes after [budget] of them, the way a
/// process that dies stops writing. A null budget keeps every write.
final class CrashingStore implements SaveStore {
  CrashingStore(this.inner);

  final SaveStore inner;
  int? budget;

  @override
  Future<void> write(String key, SaveRecord record) async {
    final left = budget;
    if (left != null) {
      if (left <= 0) return;
      budget = left - 1;
    }
    await inner.write(key, record);
  }

  @override
  Future<SaveRecord?> read(String key) => inner.read(key);

  @override
  Future<bool> exists(String key) => inner.exists(key);

  @override
  Future<List<String>> keys() => inner.keys();
}

/// One server and the clients on it.
final class Rig {
  Rig({SaveStore? store, DateTime Function()? clock, this.map}) {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: logged.add,
      map: map ?? openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      story: defaultStory,
      shops: defaultShops,
      combat: defaultCombat,
      storage: store == null
          ? null
          : Storage(
              queue: SaveQueue.inline(
                store,
                clock: clock ?? steppingClock(),
                onFailure: (_, _) {},
              ),
            ),
    );
  }

  final TileMap? map;
  late final ScapeServer server;
  final List<String> logged = [];
  final List<TestClient> clients = [];
  bool _stopped = false;

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  Future<TestClient> join(String name) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await tick(2);
    return client;
  }

  /// Joins [first] and [second] and stands them next to each other.
  Future<(TestClient, TestClient)> pair(String first, String second) async {
    final a = await join(first);
    final b = await join(second);
    await b.send(Walk(at(33, 32)));
    await tick(3);
    return (a, b);
  }

  int idOf(String player) => server.movement.standing(player)!.networkId;

  Container bag(String player) =>
      server.items.container(player, ContainerId.inventory)!;

  /// Both players ask for the trade, and it opens.
  Future<void> open(
    TestClient a,
    TestClient b,
    String first,
    String second,
  ) async {
    await a.click(idOf(second));
    await tick(2);
    await b.click(idOf(first));
    await tick(2);
  }

  Future<void> stop() async {
    if (_stopped) return;
    _stopped = true;
    for (final client in clients) {
      await client.dispose();
    }
    await server.dispose();
  }
}

void main() {
  final rigs = <Rig>[];

  Rig start({SaveStore? store, DateTime Function()? clock}) {
    final rig = Rig(store: store, clock: clock);
    rigs.add(rig);
    return rig;
  }

  tearDown(() async {
    for (final rig in rigs) {
      await rig.stop();
    }
    rigs.clear();
  });

  group('a request', () {
    test('is told to both, and opens the trade when it is returned', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');

      await ana.click(rig.idOf('bo'));
      await rig.tick(2);
      expect(ana.notices, contains('Sending a trade request to bo.'));
      expect(bo.notices, contains('ana wants to trade with you.'));
      expect(ana.views, isEmpty);

      await bo.click(rig.idOf('ana'));
      await rig.tick(2);

      expect(ana.views.last.partner, 'bo');
      expect(bo.views.last.partner, 'ana');
    });

    test('is dropped after a minute', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await ana.click(rig.idOf('bo'));
      await rig.tick(105);

      await bo.click(rig.idOf('ana'));
      await rig.tick(2);

      expect(ana.views, isEmpty);
      expect(bo.notices, contains('Sending a trade request to ana.'));
    });

    test('is never told to a player who ignores the sender', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await bo.send(EditList(list: ListKind.ignored, add: true, name: 'ana'));

      await ana.click(rig.idOf('bo'));
      await rig.tick(2);
      await bo.click(rig.idOf('ana'));
      await rig.tick(2);

      expect(ana.notices, contains('Sending a trade request to bo.'));
      expect(bo.notices, isNot(contains('ana wants to trade with you.')));
      expect(ana.views, isEmpty);
    });

    test('is refused for a player already in a trade', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      final cy = await rig.join('Cy');
      await cy.send(Walk(at(31, 32)));
      await rig.tick(3);
      await rig.open(ana, bo, 'ana', 'bo');

      await cy.click(rig.idOf('ana'));
      await rig.tick(2);

      expect(cy.notices, contains('That player is busy.'));
    });
  });

  group('a trade', () {
    test('shows each side what both have offered', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      rig.bag('ana').add(ItemId.logs, 3);
      await rig.open(ana, bo, 'ana', 'bo');

      await ana.offer(ItemId.logs, 2);
      await rig.tick();

      expect(ana.views.last.mine, [ItemStack(ItemId.logs, 2)]);
      expect(bo.views.last.theirs, [ItemStack(ItemId.logs, 2)]);
      expect(bo.views.last.mine, isEmpty);
    });

    test('swaps what was offered once both accept twice', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      rig.bag('ana').add(ItemId.logs, 3);
      rig.bag('bo').add(ItemId.feathers, 2);
      await rig.open(ana, bo, 'ana', 'bo');
      await ana.offer(ItemId.logs, 3);
      await bo.offer(ItemId.feathers, 2);

      for (var step = 0; step < 2; step++) {
        await ana.send(const TradeAccept());
        await bo.send(const TradeAccept());
        await rig.tick();
      }

      expect(rig.bag('ana').countOf(ItemId.logs), 0);
      expect(rig.bag('ana').countOf(ItemId.feathers), 2);
      expect(rig.bag('bo').countOf(ItemId.logs), 3);
      expect(rig.bag('bo').countOf(ItemId.feathers), 0);
      expect(ana.closes, 1);
      expect(bo.closes, 1);
      expect(ana.notices, contains('The trade is complete.'));
      expect(
        rig.logged.single,
        'Trade: ana gave 3 x Logs, bo gave 2 x Feathers',
      );
    });

    test('leaves its journal empty once it is saved', () async {
      final store = MemorySaveStore();
      final rig = start(store: store);
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await rig.open(ana, bo, 'ana', 'bo');

      for (var step = 0; step < 2; step++) {
        await ana.send(const TradeAccept());
        await bo.send(const TradeAccept());
        await rig.tick();
      }
      await rig.stop();

      expect((await store.read(tradeKeyOf('ana', 'bo')))!.bytes, isEmpty);
    });

    test('moves to the confirm step after the first accepts', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await rig.open(ana, bo, 'ana', 'bo');

      await ana.send(const TradeAccept());
      await bo.send(const TradeAccept());
      await rig.tick();

      expect(ana.views.last.confirming, isTrue);
      expect(ana.views.last.youAccepted, isFalse);
    });

    test('loses an accept when either side changes an offer', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      rig.bag('ana').add(ItemId.logs, 3);
      await rig.open(ana, bo, 'ana', 'bo');
      await ana.send(const TradeAccept());
      await rig.tick();
      expect(bo.views.last.theyAccepted, isTrue);

      await ana.offer(ItemId.logs, 1);
      await rig.tick();

      expect(bo.views.last.theyAccepted, isFalse);
      expect(ana.views.last.youAccepted, isFalse);
    });

    test('refuses to offer more than the player holds', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      rig.bag('ana').add(ItemId.logs, 3);
      await rig.open(ana, bo, 'ana', 'bo');

      await ana.offer(ItemId.logs, 4);
      await rig.tick();

      expect(ana.notices, contains('You do not have that many.'));
      expect(ana.views.last.mine, isEmpty);
    });

    test('stays open and tells both when a bag has no room', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      rig.bag('ana').add(ItemId.logs, 3);
      while (rig.bag('bo').occupied().length < inventorySize) {
        rig.bag('bo').add(ItemId.bones, 1);
      }
      await rig.open(ana, bo, 'ana', 'bo');
      await ana.offer(ItemId.logs, 3);

      for (var step = 0; step < 2; step++) {
        await ana.send(const TradeAccept());
        await bo.send(const TradeAccept());
        await rig.tick();
      }

      expect(rig.bag('ana').countOf(ItemId.logs), 3);
      expect(rig.bag('bo').countOf(ItemId.logs), 0);
      expect(
        bo.notices,
        contains('The trade cannot go through. You do not have room for that.'),
      );
      expect(
        ana.notices,
        contains(
          'The trade cannot go through. bo does not have room for that.',
        ),
      );
      expect(ana.views.last.confirming, isFalse);
      expect(ana.closes, 0);
    });

    test('is cancelled for both when one walks away', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await rig.open(ana, bo, 'ana', 'bo');

      await bo.send(Walk(at(45, 32)));
      await rig.tick(8);

      expect(ana.closes, 1);
      expect(bo.closes, 1);
      expect(ana.notices, contains('The trade was cancelled.'));
    });

    test('is declined by either player', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await rig.open(ana, bo, 'ana', 'bo');

      await bo.send(const TradeDecline());
      await rig.tick();

      expect(ana.notices, contains('bo declined the trade.'));
      expect(ana.closes, 1);
    });

    test('ends and tells the partner when a player leaves', () async {
      final rig = start();
      final (ana, bo) = await rig.pair('Ana', 'Bo');
      await rig.open(ana, bo, 'ana', 'bo');

      await bo.link.close();
      await rig.tick(12);

      expect(ana.notices, contains('bo left the trade.'));
      expect(ana.closes, 1);
    });
  });

  group('a crash in a trade', () {
    /// Ana holds 3 logs and Bo 2 feathers, both saved. They trade, and the
    /// store keeps only [kept] of the writes that follow the last accept. A
    /// new server then reads both players.
    Future<({Rig rig, Container ana, Container bo})> crashed(
      int kept, {
      required CrashingStore store,
      required DateTime Function() clock,
    }) async {
      final setup = start(store: store, clock: clock);
      await setup.join('Ana');
      await setup.join('Bo');
      setup.bag('ana').add(ItemId.logs, 3);
      setup.bag('bo').add(ItemId.feathers, 2);
      await setup.stop();

      final trading = start(store: store, clock: clock);
      final (ana, bo) = await trading.pair('Ana', 'Bo');
      await trading.open(ana, bo, 'ana', 'bo');
      await ana.offer(ItemId.logs, 3);
      await bo.offer(ItemId.feathers, 2);
      await ana.send(const TradeAccept());
      await bo.send(const TradeAccept());
      await trading.tick();
      await ana.send(const TradeAccept());
      store.budget = kept;
      await bo.send(const TradeAccept());
      await trading.tick();
      await trading.stop();

      store.budget = null;
      final after = start(store: store, clock: clock);
      await after.join('Ana');
      await after.join('Bo');
      return (rig: after, ana: after.bag('ana'), bo: after.bag('bo'));
    }

    for (var kept = 0; kept <= 4; kept++) {
      test('after $kept of 4 writes leaves the items whole', () async {
        final found = await crashed(
          kept,
          store: CrashingStore(MemorySaveStore()),
          clock: steppingClock(),
        );

        expect(found.ana.countOf(ItemId.logs), kept == 0 ? 3 : 0);
        expect(found.bo.countOf(ItemId.logs), kept == 0 ? 0 : 3);
        expect(found.ana.countOf(ItemId.feathers), kept == 0 ? 0 : 2);
        expect(found.bo.countOf(ItemId.feathers), kept == 0 ? 2 : 0);
      });
    }

    test('is finished once, and not again on the next start', () async {
      final store = CrashingStore(MemorySaveStore());
      final clock = steppingClock();
      final first = await crashed(1, store: store, clock: clock);
      await first.rig.stop();

      final second = start(store: store, clock: clock);
      await second.join('Ana');

      expect(
        first.rig.logged.where((line) => line.contains('cut short')),
        hasLength(2),
      );
      expect(second.logged, isEmpty);
    });
  });

  group('a trade journal', () {
    /// A store holding a save for Ana and Bo, with Ana's skill at [xp].
    Future<(MemorySaveStore, Uint8List, Uint8List)> saved(int xp) async {
      final store = MemorySaveStore();
      final rig = start(store: store);
      await rig.join('Ana');
      await rig.join('Bo');
      rig.server.skills.grant('ana', 'mining', xp);
      await rig.stop();
      return (
        store,
        (await store.read(saveKeyOf('ana')))!.bytes,
        (await store.read(saveKeyOf('bo')))!.bytes,
      );
    }

    Future<void> journal(
      MemorySaveStore store,
      Map<String, Uint8List> states,
      DateTime when,
    ) => store.write(
      tradeKeyOf('ana', 'bo'),
      SaveRecord(
        bytes: encodeJournal(states),
        reason: Custom('trade'),
        at: when,
      ),
    );

    test('brings a save older than it up to it', () async {
      final (store, older, bo) = await saved(100);
      final newer = start(store: store);
      await newer.join('Ana');
      newer.server.skills.grant('ana', 'mining', 800);
      await newer.stop();
      await journal(store, {'ana': older, 'bo': bo}, DateTime.utc(2100));

      final rig = start(store: store);
      await rig.join('Ana');

      expect(rig.server.skills.xpOf('ana', 'mining'), 100);
    });

    test('leaves a save newer than it alone', () async {
      final (store, older, bo) = await saved(100);
      final newer = start(store: store);
      await newer.join('Ana');
      newer.server.skills.grant('ana', 'mining', 800);
      await newer.stop();
      await journal(store, {'ana': older, 'bo': bo}, DateTime.utc(2020));

      final rig = start(store: store);
      await rig.join('Ana');

      expect(rig.server.skills.xpOf('ana', 'mining'), 900);
    });

    test('that is damaged is logged and does not stop a login', () async {
      final store = MemorySaveStore();
      await store.write(
        tradeKeyOf('ana', 'bo'),
        SaveRecord(
          bytes: Uint8List.fromList([1, 2, 3]),
          reason: Custom('trade'),
          at: DateTime.utc(2026),
        ),
      );
      final rig = start(store: store);

      final ana = await rig.join('Ana');

      expect(ana.admitted, isTrue);
      expect(rig.logged.single, contains('is damaged and was left'));
    });

    test('names two players and nobody else', () {
      final one = Uint8List.fromList('{"ana":"AA=="}'.codeUnits);
      final stranger = Uint8List.fromList(
        '{"ana":"AA==","../x":"AA=="}'.codeUnits,
      );

      expect(() => decodeJournal(one), throwsFormatException);
      expect(() => decodeJournal(stranger), throwsFormatException);
      expect(
        decodeJournal(encodeJournal({'ana': Uint8List(1), 'bo': Uint8List(2)})),
        {'ana': Uint8List(1), 'bo': Uint8List(2)},
      );
    });

    test('has one key for a pair, whichever way round', () {
      expect(tradeKeyOf('ana', 'bo'), tradeKeyOf('bo', 'ana'));
      expect(tradeKeyOf('ab', 'c'), isNot(tradeKeyOf('a', 'bc')));
    });
  });
}
