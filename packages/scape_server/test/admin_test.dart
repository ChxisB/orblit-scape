import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:scape_server/src/features/admin/ledger.dart';
import 'package:scape_server/src/features/admin/rollback.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/open_world.dart';
import 'support/storage.dart';
import 'support/test_client.dart';

const String _password = 'correct horse';

const Duration _disk = Duration(milliseconds: 100);

/// A clock the test moves by hand. Each read is a millisecond later, so two
/// saves never share an instant.
final class _Clock {
  Duration offset = Duration.zero;
  int _reads = 0;

  DateTime read() =>
      DateTime.utc(2026).add(offset).add(Duration(milliseconds: _reads++));
}

/// Allows a few ticks and then throws, so a loop that should have stopped
/// fails the test and does not hang it.
final class _LimitedPacer implements Pacer {
  int allowed = 0;

  @override
  Future<void> untilNext() async {
    if (allowed-- <= 0) throw StateError('The loop did not stop.');
  }
}

/// A store that lets the test act in the middle of a read.
final class _HookedStore implements SaveStore {
  _HookedStore(this.inner);

  final SaveStore inner;
  Future<void> Function(String key)? onRead;

  @override
  Future<SaveRecord?> read(String key) async {
    final found = await inner.read(key);
    await onRead?.call(key);
    return found;
  }

  @override
  Future<void> write(String key, SaveRecord record) => inner.write(key, record);

  @override
  Future<bool> exists(String key) => inner.exists(key);

  @override
  Future<List<String>> keys() => inner.keys();
}

extension on TestClient {
  List<String> get notices => [
    for (final n in messages.whereType<Notice>()) n.text,
  ];
  List<Said> get heard => messages.whereType<Said>().toList();
}

void main() {
  final servers = <ScapeServer>[];
  final clients = <TestClient>[];
  late FastHasher hasher;
  late SaveStore store;
  late _Clock clock;
  late _LimitedPacer pacer;
  late List<String> logged;

  ScapeServer start({
    Map<String, Role> roles = const {'Ana': Role.admin, 'Mo': Role.moderator},
    bool accounts = true,
    HistoryStore? history,
  }) {
    final server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: pacer,
      log: logged.add,
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      story: defaultStory,
      shops: defaultShops,
      combat: defaultCombat,
      roles: roles,
      storage: Storage(
        queue: SaveQueue.inline(store, clock: clock.read, onFailure: (_, _) {}),
        hasher: accounts ? hasher : null,
        history: history,
        now: clock.read,
      ),
    );
    servers.add(server);
    return server;
  }

  Future<TestClient> enter(
    ScapeServer server,
    String name, {
    bool create = false,
    Duration wait = Duration.zero,
  }) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name, password: _password, create: create);
    await Future<void>.delayed(wait);
    server.loop.runTick();
    await pump();
    return client;
  }

  /// Sends [text] and ticks twice: once to run it and once to deliver what
  /// the command told the player.
  Future<void> say(
    ScapeServer server,
    TestClient who,
    String text, {
    Duration wait = Duration.zero,
  }) async {
    await who.send(Chat(text));
    server.loop.runTick();
    await pump();
    await Future<void>.delayed(wait);
    server.loop.runTick();
    await pump();
  }

  Future<void> tick(ScapeServer server, [int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  /// A server with accounts for Ana (admin), Mo (moderator) and the others,
  /// all online. A restart builds a second server on the same store.
  Future<Map<String, TestClient>> crowd(
    ScapeServer server,
    List<String> names, {
    bool create = true,
    Duration wait = Duration.zero,
  }) async => {
    for (final name in names)
      name: await enter(server, name, create: create, wait: wait),
  };

  setUp(() {
    hasher = FastHasher();
    store = MemorySaveStore();
    clock = _Clock();
    pacer = _LimitedPacer();
    logged = [];
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    for (final server in servers) {
      await server.dispose();
    }
    clients.clear();
    servers.clear();
  });

  group('roles', () {
    test('need accounts, because a name alone proves nothing', () {
      expect(() => start(accounts: false), throwsArgumentError);
    });

    test(
      'a player who holds none is told the command does not exist',
      () async {
        final server = start();
        final who = await crowd(server, ['Ana', 'Bo']);

        await say(server, who['Bo']!, '/kick ana');

        expect(who['Bo']!.notices, contains('Unknown command.'));
        expect(who['Ana']!.goodbyes, isEmpty);
        expect(who['Ana']!.heard, isEmpty);
        expect(logged, contains('Admin: bo was refused "kick"'));
      },
    );

    test('a moderator runs a moderator command and not an admin one', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);

      await say(server, who['Mo']!, '/give bo 2');
      await say(server, who['Mo']!, '/mute bo 5');

      expect(who['Mo']!.notices, [
        'Unknown command.',
        'bo is muted for 5 minutes.',
      ]);
    });

    test('a moderator cannot sanction an admin or another moderator', () async {
      final server = start();
      final who = await crowd(server, ['Ana', 'Mo']);

      await say(server, who['Mo']!, '/ban ana 0');
      await say(server, who['Mo']!, '/kick mo');
      await say(server, who['Mo']!, '/mute ana 5');

      expect(who['Mo']!.notices, [
        'You cannot do that to ana.',
        'You cannot do that to mo.',
        'You cannot do that to ana.',
      ]);
      expect(who['Ana']!.goodbyes, isEmpty);
    });

    test('an admin may sanction a moderator, who cannot lift it', () async {
      final server = start();
      final who = await crowd(server, ['Ana', 'Mo']);

      await say(server, who['Ana']!, '/mute mo 5');
      await say(server, who['Mo']!, '/unmute mo');

      expect(who['Ana']!.notices, ['mo is muted for 5 minutes.']);
      expect(
        who['Mo']!.notices.last,
        'You cannot lift a sanction on yourself.',
      );
    });

    test('are set by name whatever the case', () async {
      final server = start(roles: {'ANA': Role.admin});
      final who = await crowd(server, ['Ana']);

      await say(server, who['Ana']!, '/cancelshutdown');

      expect(who['Ana']!.notices, ['No shutdown is set.']);
    });
  });

  group('commands', () {
    test('bad input is told how to write it, and never throws', () async {
      final server = start();
      final who = await crowd(server, ['Ana', 'Mo', 'Bo']);
      const cases = [
        ('Mo', '/', 'Unknown command.'),
        ('Mo', '/mute', 'Use /mute'),
        ('Mo', '/mute bo', 'Use /mute'),
        ('Mo', '/mute bo soon', 'Use /mute'),
        ('Mo', '/mute bo -5', 'Use /mute'),
        ('Mo', '/mute bo 99999999999999', 'Use /mute'),
        ('Mo', '/mute bo 0x10', 'Use /mute'),
        ('Mo', '/mute bo +5', 'Use /mute'),
        ('Mo', '/mute bo -0', 'Use /mute'),
        ('Mo', '/ban "Ana 5', 'is not a valid name.'),
        ('Mo', '/kick nobody', 'nobody is not online.'),
        ('Mo', '/unmute bo', 'bo is not muted.'),
        ('Mo', '/unban bo', 'bo is not banned.'),
        ('Ana', '/give bo 9999', 'There is no such item.'),
        ('Ana', '/skill bo nothing 5', 'There is no such skill.'),
        ('Ana', '/teleport bo 99999 99999', 'That tile is outside the world.'),
        ('Ana', '/shutdown 0', 'Use /shutdown'),
        ('Ana', '/cancelshutdown', 'No shutdown is set.'),
        ('Ana', '/rollback bo 5', 'This server keeps no save history.'),
        ('Ana', '/broadcast', 'Use /broadcast'),
      ];

      for (final (name, line, expected) in cases) {
        await say(server, who[name]!, line);
        expect(who[name]!.notices.last, contains(expected), reason: line);
      }
    });

    test('a quoted name may hold spaces', () async {
      final server = start();
      final who = await crowd(server, ['Mo']);

      await say(server, who['Mo']!, '/mute "Bo Bo" 5');

      expect(who['Mo']!.notices, ['bo bo is muted for 5 minutes.']);
    });

    test('each one does its work and writes a log line', () async {
      final server = start();
      final who = await crowd(server, ['Ana', 'Mo', 'Bo', 'Di']);
      final lines = [
        ('Mo', '/mute bo 5 loud', 'Admin: mo mute bo: loud, for 5 minutes'),
        ('Mo', '/unmute bo', 'Admin: mo unmute bo: lifted'),
        ('Mo', '/ban cy 0', 'Admin: mo ban cy: no reason given, with no end'),
        ('Mo', '/unban cy', 'Admin: mo unban cy: lifted'),
        ('Ana', '/teleport bo 40 41', 'Admin: ana teleport bo: to 40 41 0'),
        (
          'Ana',
          '/give bo ${ItemId.logs} 3',
          'Admin: ana give bo: 3 of 3 x Logs',
        ),
        ('Ana', '/skill bo fishing 250', 'Admin: ana skill bo: fishing to 250'),
        (
          'Ana',
          '/broadcast hello all',
          'Admin: ana broadcast everyone: hello all',
        ),
        ('Ana', '/shutdown 30', 'Admin: ana shutdown everyone: in 30 seconds'),
        (
          'Ana',
          '/cancelshutdown',
          'Admin: ana cancelshutdown everyone: cancelled',
        ),
        ('Mo', '/kick di spam', 'Admin: mo kick di: spam'),
      ];

      for (final (name, line, entry) in lines) {
        await say(server, who[name]!, line);
        expect(logged, contains(entry), reason: line);
      }

      expect(server.movement.standing('bo')!.tile, TileCoord(40, 41, 0));
      expect(
        server.items
            .container('bo', ContainerId.inventory)!
            .countOf(ItemId.logs),
        3,
      );
      expect(server.skills.xpOf('bo', 'fishing'), 250);
      expect(who['Bo']!.notices, contains('[Server] hello all'));
      expect(who['Di']!.notices, contains('You were kicked: spam'));
      expect(who['Di']!.goodbyes, isNotEmpty);
    });

    test('a kick is not a ban', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);
      await say(server, who['Mo']!, '/kick bo');

      final again = await enter(server, 'Bo');

      expect(again.admitted, isTrue);
    });

    test('a line that is a command is never said aloud', () async {
      final server = start();
      final who = await crowd(server, ['Bo', 'Cy']);

      await say(server, who['Bo']!, '/hello');
      await say(server, who['Bo']!, 'hello');

      expect([for (final said in who['Cy']!.heard) said.text], ['hello']);
    });
  });

  group('mutes', () {
    test('hold across a restart', () async {
      final first = start();
      final before = await crowd(first, ['Mo', 'Bo']);
      await say(first, before['Bo']!, 'before');
      await say(first, before['Mo']!, '/mute bo 0 rude');
      await say(first, before['Bo']!, 'muted');
      await first.dispose();

      final second = start();
      final after = await crowd(second, ['Mo', 'Bo'], create: false);
      await say(second, after['Bo']!, 'after');

      expect([for (final said in before['Mo']!.heard) said.text], ['before']);
      expect(after['Mo']!.heard, isEmpty);
      expect(before['Bo']!.notices, contains('You are muted: rude'));
      expect(after['Bo']!.notices, contains('You are muted.'));
    });

    test('end by themselves', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);
      await say(server, who['Mo']!, '/mute bo 5');
      await say(server, who['Bo']!, 'quiet');
      expect(who['Mo']!.heard, isEmpty);

      clock.offset = const Duration(minutes: 6);
      await tick(server);
      await say(server, who['Bo']!, 'loud');

      expect(who['Bo']!.notices, contains('You can chat again.'));
      expect([for (final said in who['Mo']!.heard) said.text], ['loud']);
    });

    test('apply to a line sent in the same tick', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);

      await who['Mo']!.send(Chat('/mute bo 0'));
      await who['Bo']!.send(Chat('too late'));
      await tick(server, 2);

      expect(who['Mo']!.heard, isEmpty);
    });

    test('are lifted by unmute', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);
      await say(server, who['Mo']!, '/mute bo 0');

      await say(server, who['Mo']!, '/unmute bo');
      await say(server, who['Bo']!, 'free');

      expect([for (final said in who['Mo']!.heard) said.text], ['free']);
    });
  });

  group('bans', () {
    test('keep a player out after a restart, until they are lifted', () async {
      final first = start();
      final before = await crowd(first, ['Mo', 'Bo']);
      await say(first, before['Mo']!, '/ban bo 0 cheating');
      expect(before['Bo']!.goodbyes, isNotEmpty);
      await first.dispose();

      final second = start();
      final mo = await enter(second, 'Mo');
      final refused = await enter(second, 'Bo');
      await say(second, mo, '/unban bo');
      final allowed = await enter(second, 'Bo');

      expect(refused.goodbyes, [LoginRefusal.banned.name]);
      expect(allowed.admitted, isTrue);
    });

    test('that land while a login reads the save still stop it', () async {
      final hooked = _HookedStore(MemorySaveStore());
      store = hooked;
      final server = start();
      final mo = await enter(server, 'Mo', create: true);
      hooked.onRead = (key) async {
        if (key != saveKeyOf('bo')) return;
        hooked.onRead = null;
        await mo.send(Chat('/ban bo 0'));
        await tick(server);
      };

      final bo = await enter(server, 'Bo', create: true);

      expect(bo.goodbyes, [LoginRefusal.banned.name]);
    });

    test('with a length end by themselves', () async {
      final server = start();
      final who = await crowd(server, ['Mo', 'Bo']);
      await say(server, who['Mo']!, '/ban bo 10');
      clock.offset = const Duration(minutes: 11);

      final again = await enter(server, 'Bo');

      expect(again.admitted, isTrue);
    });

    test('are checked after the password', () async {
      final first = start();
      final before = await crowd(first, ['Mo', 'Bo']);
      await say(first, before['Mo']!, '/ban bo 0');
      await first.dispose();
      final second = start();

      final guess = TestClient(second);
      clients.add(guess);
      await guess.login('Bo', password: 'wrong horse');

      expect(guess.goodbyes, [LoginRefusal.badLogin.name]);
    });

    test(
      'a damaged ledger keeps everyone out and is not written over',
      () async {
        final bytes = Uint8List.fromList(utf8.encode('not json'));
        await store.write(
          ledgerKey,
          SaveRecord(bytes: bytes, reason: Custom('test'), at: clock.read()),
        );
        final server = start();

        final who = await enter(server, 'Bo', create: true);

        expect(who.goodbyes, [LoginRefusal.unavailable.name]);
        expect((await store.read(ledgerKey))!.bytes, bytes);
      },
    );
  });

  group('shutdown', () {
    test('counts down, tells everyone, then stops the loop', () async {
      final server = start();
      final who = await crowd(server, ['Ana', 'Bo']);
      await say(server, who['Ana']!, '/shutdown 3');

      pacer.allowed = 20;
      await server.loop.run();
      await pump();

      expect(pacer.allowed, greaterThan(10));
      expect(who['Bo']!.notices, [
        '[Server] The server shuts down in 3 seconds.',
        '[Server] The server shuts down in 2 seconds.',
        '[Server] The server shuts down in 1 second.',
      ]);
    });

    test('does not stop once it is cancelled', () async {
      final server = start();
      final who = await crowd(server, ['Ana']);
      await say(server, who['Ana']!, '/shutdown 3');
      await say(server, who['Ana']!, '/cancelshutdown');

      pacer.allowed = 20;

      await expectLater(server.loop.run(), throwsStateError);
    });
  });

  group('rollback', () {
    late Directory dir;
    late HistoryStore history;

    setUp(() {
      dir = Directory.systemTemp.createTempSync('scape_admin');
      history = HistoryStore.files(dir.path);
      store = history;
    });

    tearDown(() => dir.deleteSync(recursive: true));

    Future<void> until(bool Function() done) async {
      for (var i = 0; i < 50 && !done(); i++) {
        await Future<void>.delayed(const Duration(milliseconds: 20));
      }
    }

    test('logs the player out first, then puts their save back', () async {
      final first = start(history: history);
      final bo = await enter(first, 'Bo', create: true, wait: _disk);
      first.skills.grant('bo', 'fishing', 100);
      await bo.link.close();
      await tick(first, 12);
      await Future<void>.delayed(_disk);
      clock.offset = const Duration(minutes: 30);
      final back = await enter(first, 'Bo', wait: _disk);
      first.skills.grant('bo', 'fishing', 900);
      final ana = await enter(first, 'Ana', create: true, wait: _disk);

      await ana.send(Chat('/rollback bo 20'));
      await tick(first);
      await until(() => logged.any((l) => l.startsWith('Admin: ana rollback')));
      await tick(first);
      final again = await enter(first, 'Bo', wait: _disk);

      expect(back.notices.first, startsWith('You are logged out'));
      expect(back.goodbyes, isNotEmpty);
      expect(ana.notices.last, startsWith('bo was rolled back to 2026-01-01'));
      expect(first.skills.xpOf('bo', 'fishing'), 100);
      expect(again.admitted, isTrue);
    });
  });

  group('Rollback', () {
    late Directory dir;
    late HistoryStore history;
    late Ledger ledger;
    late List<bool> lockedAtLogOut;

    Rollback make({Future<void> Function(String player)? logOut}) {
      final queue = SaveQueue.inline(
        history,
        clock: clock.read,
        onFailure: (_, _) {},
      );
      ledger = Ledger(queue: queue, now: clock.read);
      return Rollback(
        history: history,
        queue: queue,
        ledger: ledger,
        logOut:
            logOut ??
            (player) async => lockedAtLogOut.add(ledger.isLocked(player)),
        isOnline: (_) => false,
        now: clock.read,
      );
    }

    setUp(() {
      dir = Directory.systemTemp.createTempSync('scape_rollback');
      history = HistoryStore.files(dir.path);
      lockedAtLogOut = [];
    });

    tearDown(() => dir.deleteSync(recursive: true));

    test(
      'locks the player while it runs and says when nothing is old enough',
      () async {
        final rollback = make();

        final said = await rollback.run('bo', 5);

        expect(lockedAtLogOut, [true]);
        expect(ledger.isLocked('bo'), isFalse);
        expect(said, 'bo has no save from 5 minutes ago.');
      },
    );

    test('refuses a second rollback of a name that is locked', () async {
      final release = Completer<void>();
      final rollback = make(logOut: (_) => release.future);
      final first = rollback.run('bo', 5);

      final second = await rollback.run('bo', 20);
      release.complete();
      await first;

      expect(second, 'A rollback of bo is already running.');
      expect(ledger.isLocked('bo'), isFalse);
    });

    test('answers a failure and still unlocks', () async {
      final rollback = make(logOut: (_) async => throw StateError('stuck'));

      final said = await rollback.run('bo', 5);

      expect(said, startsWith('The rollback of bo failed: '));
      expect(ledger.isLocked('bo'), isFalse);
    });
  });

  group('Ledger', () {
    late SaveQueue queue;

    Ledger make() => Ledger(queue: queue, now: clock.read);

    setUp(() {
      queue = SaveQueue.inline(store, clock: clock.read, onFailure: (_, _) {});
    });

    test('keeps sanctions across a reload, with who and why', () async {
      final first = make();
      await first.load();
      first.add(
        SanctionKind.mute,
        'bo',
        by: 'mo',
        reason: 'rude',
        lasting: const Duration(minutes: 5),
      );
      first.add(SanctionKind.ban, 'cy', by: 'ana', reason: 'cheat');
      await queue.flush();

      final second = make();
      await second.load();

      final mute = second.active(SanctionKind.mute, 'bo')!;
      expect((mute.by, mute.reason), ('mo', 'rude'));
      expect(second.active(SanctionKind.ban, 'cy')!.until, isNull);
      expect(second.active(SanctionKind.ban, 'bo'), isNull);
      clock.offset = const Duration(minutes: 6);
      expect(second.active(SanctionKind.mute, 'bo'), isNull);
    });

    test('drops a sanction that has ended when it saves', () async {
      final ledger = make();
      await ledger.load();
      ledger.add(
        SanctionKind.mute,
        'bo',
        by: 'mo',
        reason: 'x',
        lasting: const Duration(minutes: 5),
      );
      clock.offset = const Duration(minutes: 6);

      ledger.add(SanctionKind.ban, 'cy', by: 'ana', reason: 'cheat');
      await queue.flush();

      final saved =
          jsonDecode(utf8.decode((await store.read(ledgerKey))!.bytes))
              as Map<String, Object?>;
      expect(saved['mute'], isEmpty);
      expect(saved['ban'], contains('cy'));
    });

    test('lift removes a sanction for good', () async {
      final first = make();
      await first.load();
      first.add(SanctionKind.ban, 'cy', by: 'ana', reason: 'cheat');
      first.lift(SanctionKind.ban, 'cy');
      await queue.flush();

      final second = make();
      await second.load();

      expect(second.active(SanctionKind.ban, 'cy'), isNull);
    });

    test('refuses a change before it has loaded', () {
      expect(
        () => make().add(SanctionKind.ban, 'cy', by: 'ana', reason: 'x'),
        throwsStateError,
      );
    });

    test('a damaged record fails every load until it is fixed', () async {
      await store.write(
        ledgerKey,
        SaveRecord(
          bytes: Uint8List.fromList(utf8.encode('{"v":2}')),
          reason: Custom('test'),
          at: clock.read(),
        ),
      );
      final ledger = make();

      await expectLater(ledger.load(), throwsFormatException);
      await expectLater(ledger.load(), throwsFormatException);
    });
  });
}
