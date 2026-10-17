import 'dart:async';
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

const String _password = 'correct horse';

void main() {
  final servers = <ScapeServer>[];
  final clients = <TestClient>[];
  late FastHasher hasher;
  late MemorySaveStore store;

  ScapeServer start() {
    final server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      shops: defaultShops,
      combat: defaultCombat,
      storage: Storage(
        queue: SaveQueue.inline(
          store,
          clock: steppingClock(),
          onFailure: (_, _) {},
        ),
        hasher: hasher,
      ),
    );
    servers.add(server);
    return server;
  }

  Future<TestClient> enter(
    ScapeServer server,
    String name, {
    String password = _password,
    bool create = false,
  }) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name, password: password, create: create);
    server.loop.runTick();
    await pump();
    return client;
  }

  setUp(() {
    hasher = FastHasher();
    store = MemorySaveStore();
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

  test('a new account joins and the same password gets back in', () async {
    final server = start();
    final first = await enter(server, 'Ana', create: true);
    expect(first.admitted, isTrue);
    await first.link.close();
    for (var i = 0; i < 12; i++) {
      server.loop.runTick();
    }
    await pump();

    final again = await enter(server, 'Ana');

    expect(again.admitted, isTrue);
  });

  test('an account outlives the server', () async {
    final first = start();
    await enter(first, 'Ana', create: true);
    await first.dispose();

    final second = start();
    final ana = await enter(second, 'Ana');
    final other = await enter(second, 'Bo');

    expect(ana.admitted, isTrue);
    expect(other.goodbyes, [LoginRefusal.badLogin.name]);
  });

  test('a wrong password is a bad login', () async {
    final server = start();
    await enter(server, 'Ana', create: true);
    await server.dispose();
    final second = start();

    final ana = await enter(second, 'Ana', password: 'wrong horse');

    expect(ana.admitted, isFalse);
    expect(ana.goodbyes, [LoginRefusal.badLogin.name]);
  });

  test('a name with no account is a bad login, not a free name', () async {
    final server = start();

    final ana = await enter(server, 'Ana');

    expect(ana.goodbyes, [LoginRefusal.badLogin.name]);
    expect(hasher.calls, 0);
  });

  test('a short password cannot make an account', () async {
    final server = start();

    final ana = await enter(server, 'Ana', password: 'short', create: true);

    expect(ana.goodbyes, [LoginRefusal.weakPassword.name]);
    expect(await store.exists(accountKeyOf('ana')), isFalse);
  });

  test('a name that has an account cannot be made again', () async {
    final first = start();
    await enter(first, 'Ana', create: true);
    await first.dispose();
    final second = start();

    final again = await enter(
      second,
      'Ana',
      password: 'other horse',
      create: true,
    );

    expect(again.goodbyes, [LoginRefusal.accountExists.name]);
    expect((await enter(second, 'Ana')).admitted, isTrue);
  });

  test('logins past the limit hear busy and cost no hash', () async {
    final server = start();
    for (final name in ['Ana', 'Bo', 'Cy', 'Di', 'Ed']) {
      await enter(server, name, create: true);
    }
    await server.dispose();
    final second = start();
    hasher
      ..calls = 0
      ..gate = Completer<void>();

    final waiting = [
      for (final name in ['Ana', 'Bo', 'Cy', 'Di', 'Ed']) enter(second, name),
    ];
    final late = await Future.wait(waiting);
    hasher.gate!.complete();
    await pump();

    final refused = late.where((c) => c.goodbyes.contains('busy'));
    expect(refused, hasLength(1));
    expect(hasher.calls, 4);
  });

  test('the salt makes two accounts with one password differ', () async {
    final server = start();
    await enter(server, 'Ana', create: true);
    await enter(server, 'Bo', create: true);
    await server.dispose();

    final ana = (await store.read(accountKeyOf('ana')))!.bytes;
    final bo = (await store.read(accountKeyOf('bo')))!.bytes;

    expect(ana, isNot(bo));
  });

  test('a damaged account record refuses the login', () async {
    await store.write(
      accountKeyOf('ana'),
      SaveRecord(
        bytes: Uint8List.fromList([0]),
        reason: Custom('account'),
        at: DateTime.utc(2026),
      ),
    );
    final server = start();

    final ana = await enter(server, 'Ana');

    expect(ana.goodbyes, [LoginRefusal.unavailable.name]);
  });

  test('Argon2id gives the same hash for the same salt and no other', () async {
    final salt = Uint8List.fromList(List.generate(16, (i) => i));
    final other = Uint8List.fromList(List.generate(16, (i) => i + 1));
    const hasher = Argon2idHasher();

    final a = await hasher.hash(_password, salt);

    expect(a, hasLength(32));
    expect(await hasher.hash(_password, salt), a);
    expect(await hasher.hash(_password, other), isNot(a));
    expect(await hasher.hash('other horse', salt), isNot(a));
  });
}
