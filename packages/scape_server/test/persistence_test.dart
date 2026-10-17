import 'dart:async';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/storage.dart';
import 'support/test_client.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

/// One server and the clients on it. [stop] ends it once.
final class Rig {
  Rig(
    this.store, {
    TileMap? map,
    AutoSave? autosave,
    DateTime Function()? clock,
  }) : queue = SaveQueue.inline(
         store,
         clock: clock ?? steppingClock(),
         onFailure: (key, error) => failures.add('$key: $error'),
       ) {
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
      shops: defaultShops,
      combat: defaultCombat,
      storage: Storage(queue: queue, autosave: autosave),
    );
  }

  final SaveStore store;
  final SaveQueue queue;
  late final ScapeServer server;
  final List<String> logged = [];
  static final List<String> failures = [];
  final List<TestClient> clients = [];
  bool _stopped = false;

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  /// A file store takes real time to read, so a test that uses one gives
  /// [wait] for the login to get through it.
  Future<TestClient> join(String name, {Duration wait = Duration.zero}) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await Future<void>.delayed(wait);
    await tick();
    return client;
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

const Duration disk = Duration(milliseconds: 100);

void main() {
  final rigs = <Rig>[];

  Rig start(
    SaveStore store, {
    TileMap? map,
    AutoSave? autosave,
    DateTime Function()? clock,
  }) {
    final rig = Rig(store, map: map, autosave: autosave, clock: clock);
    rigs.add(rig);
    return rig;
  }

  tearDown(() async {
    for (final rig in rigs) {
      await rig.stop();
    }
    rigs.clear();
    Rig.failures.clear();
  });

  group('a restart', () {
    test('keeps skills, items, tile and lists', () async {
      final store = MemorySaveStore();
      final first = start(store);
      final ana = await first.join('Ana');
      first.server.skills.grant('ana', 'woodcutting', 500);
      first.server.items
          .container('ana', ContainerId.inventory)!
          .add(ItemId.logs, 3);
      first.server.social.restore('ana', friends: ['bo'], ignored: ['cy']);
      await ana.send(Walk(at(36, 32)));
      await first.tick(4);
      await first.stop();

      final second = start(store);
      await second.join('Ana');

      expect(second.server.skills.xpOf('ana', 'woodcutting'), 500);
      expect(
        second.server.items
            .container('ana', ContainerId.inventory)!
            .countOf(ItemId.logs),
        3,
      );
      expect(second.server.social.listsOf('ana').friends, ['bo']);
      expect(second.server.social.listsOf('ana').ignored, ['cy']);
      expect(second.server.movement.tileOf('ana'), at(36, 32));
    });

    test('puts a player whose tile is now blocked at the spawn', () async {
      final store = MemorySaveStore();
      final first = start(store);
      final ana = await first.join('Ana');
      await ana.send(Walk(at(36, 32)));
      await first.tick(4);
      await first.stop();

      final map = openWorld()..setFlags(at(36, 32), TileFlags.blocked);
      final second = start(store, map: map);
      await second.join('Ana');

      expect(second.server.movement.tileOf('ana'), spawnTile);
    });

    test('refuses a damaged save and leaves it as it was', () async {
      final store = MemorySaveStore();
      final damaged = SaveRecord(
        bytes: Uint8List.fromList([1, 2, 3]),
        reason: const Autosave(),
        at: DateTime.utc(2026),
      );
      await store.write(saveKeyOf('ana'), damaged);
      final rig = start(store);

      final ana = await rig.join('Ana');
      await rig.stop();

      expect(ana.admitted, isFalse);
      expect(ana.goodbyes, [LoginRefusal.unavailable.name]);
      expect((await store.read(saveKeyOf('ana')))!.bytes, [1, 2, 3]);
    });
  });

  group('a login', () {
    test(
      'that comes while the old session ends gets the newest save',
      () async {
        final inner = MemorySaveStore();
        final store = GatedStore(inner);
        final rig = start(store);
        final first = await rig.join('Ana');
        rig.server.skills.grant('ana', 'mining', 400);
        await first.link.close();

        store.readGate = Completer<void>();
        final second = TestClient(rig.server);
        rig.clients.add(second);
        await second.login('Ana');
        await rig.tick(12);
        expect(rig.server.movement.tileOf('ana'), isNull);
        store.readGate!.complete();
        await pump();
        await rig.tick();

        expect(second.admitted, isTrue);
        expect(rig.server.skills.xpOf('ana', 'mining'), 400);
      },
    );

    test('that resumes a lingering session keeps what the world has', () async {
      final store = MemorySaveStore();
      final rig = start(store);
      final first = await rig.join('Ana');
      rig.server.skills.grant('ana', 'mining', 900);
      await first.link.close();
      await rig.tick(2);

      await rig.join('Ana');

      expect(rig.server.skills.xpOf('ana', 'mining'), 900);
    });
  });

  group('a save', () {
    test('a tick of 100 saves does not wait on the store', () async {
      final store = GatedStore(MemorySaveStore());
      final rig = start(
        store,
        autosave: AutoSave(
          perTick: 100,
          every: 1,
          jitter: 0,
          random: Random(1),
        ),
      );
      for (var i = 0; i < 100; i++) {
        await rig.join('P$i');
      }
      final gate = store.writeGate = Completer<void>();
      // A failed expect must not leave the stop waiting on the gate.
      addTearDown(() => gate.isCompleted ? null : gate.complete());
      final before = rig.queue.pending;

      await rig.tick();

      expect(rig.queue.pending - before, 100);
      gate.complete();
      await rig.queue.flush();
      expect(rig.queue.pending, 0);
      expect(await store.keys(), hasLength(100));
    });

    test('is written for a player who is still in the world on stop', () async {
      final store = GatedStore(MemorySaveStore());
      final rig = start(store);
      await rig.join('Ana');
      rig.server.skills.grant('ana', 'fishing', 250);
      store.writeGate = Completer<void>();
      final stopping = rig.stop();
      await pump();
      expect(await store.inner.exists(saveKeyOf('ana')), isFalse);

      store.writeGate!.complete();
      await stopping;

      expect(await store.inner.exists(saveKeyOf('ana')), isTrue);
    });

    test('can be rolled back and keeps what it replaced', () async {
      final dir = Directory.systemTemp.createTempSync('scape_saves');
      addTearDown(() => dir.deleteSync(recursive: true));
      final store = HistoryStore.files(dir.path);
      final clock = steppingClock();
      final first = start(store, clock: clock);
      await first.join('Ana', wait: disk);
      first.server.skills.grant('ana', 'cooking', 100);
      await first.stop();
      final older = (await store.history.list(saveKeyOf('ana'))).single;
      final second = start(store, clock: clock);
      await second.join('Ana', wait: disk);
      second.server.skills.grant('ana', 'cooking', 900);
      await second.stop();

      final tools = SaveTools(store: store, isLive: (_) => false, clock: clock);
      await tools.restore(saveKeyOf('ana'), older.at);
      final reasons = [
        for (final e in await store.history.list(saveKeyOf('ana')))
          e.reason.toString(),
      ];
      final third = start(store, clock: clock);
      await third.join('Ana', wait: disk);

      expect(third.server.skills.xpOf('ana', 'cooking'), 100);
      expect(reasons.any((r) => r.contains('pre-restore')), isTrue);
    });
  });
}
