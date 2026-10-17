import 'dart:math';
import 'dart:typed_data';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

void main() {
  late ScapeServer server;
  late FakeTime time;
  final clients = <TestClient>[];

  TestClient connect() {
    final client = TestClient(server);
    clients.add(client);
    return client;
  }

  setUp(() {
    time = FakeTime();
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: time.read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  test('a login is answered with the name the player is known by', () async {
    final ana = connect();
    await ana.login('Ana');

    expect(
      ana.messages.first,
      isA<LoginOk>().having((m) => m.name, 'name', 'ana'),
    );
    expect(server.host.sessionState('ana'), SessionState.live);
  });

  test('a walk after login is recorded as the player\'s intent', () async {
    final ana = connect();
    await ana.login('Ana');
    final target = TileCoord(100, 200, 1);

    await ana.send(Walk(target));

    final intents = server.inbox.drain().toList();
    expect(intents, hasLength(1));
    expect(intents.single.player, 'ana');
    expect(
      intents.single.intent,
      isA<Walk>().having((m) => m.target, 'target', target),
    );
  });

  test('chat and interactions are recorded too', () async {
    final ana = connect();
    await ana.login('Ana');

    await ana.send(Chat('hello'));
    await ana.send(Interact(kind: 1, id: 42, option: 2));

    expect(server.inbox.pending('ana'), 2);
  });

  test('a second login of the same name is refused', () async {
    final first = connect();
    await first.login('Ana');
    final second = connect();
    await second.login('Ana');

    expect(second.goodbyes, [LoginRefusal.nameTaken.name]);
    expect(second.messages, isEmpty);
    expect(first.goodbyes, isEmpty);
    expect(server.host.clientCount, 1);
  });

  test('names that differ only in case are the same name', () async {
    await connect().login('Ana');
    final second = connect();
    await second.login('ANA');
    expect(second.goodbyes, [LoginRefusal.nameTaken.name]);
  });

  test('a different name logs in beside the first', () async {
    await connect().login('Ana');
    final bo = connect();
    await bo.login('Bo');
    expect(bo.messages.first, isA<LoginOk>());
    expect(server.host.clientCount, 2);
  });

  test('a join that is not a login is refused as unreadable', () async {
    final client = connect();
    client.client.join(encodeClientMessage(Chat('hi')));
    await pump();
    final garbage = connect();
    garbage.client.join(Uint8List.fromList([1, 2, 3]));
    await pump();

    expect(client.goodbyes, [LoginRefusal.unreadable.name]);
    expect(garbage.goodbyes, [LoginRefusal.unreadable.name]);
    expect(server.host.clientCount, 0);
  });

  test('a login after the login is counted and does nothing', () async {
    final ana = connect();
    await ana.login('Ana');
    await ana.send(Login('Bo'));

    expect(server.wire.unexpected, 1);
    expect(server.inbox.pending('ana'), 0);
    expect(server.host.clientCount, 1);
  });

  test('a message over its own type\'s cap is refused and counted', () async {
    final ana = connect();
    await ana.login('Ana');
    // A walk is six bytes. This one is forty.
    final walk = Uint8List(40)..[0] = 2;

    await ana.sendBytes(walk);

    expect(server.wire.refused[BadMessage.tooLarge], 1);
    expect(server.inbox.pending('ana'), 0);
  });

  test('a message past the host budget never reaches the decoder', () async {
    final ana = connect();
    await ana.login('Ana');
    // One byte more than the longest chat line, once wrapped.
    final chat = Uint8List(2 + maxTextBytes + 1)
      ..[0] = 4
      ..[1] = maxTextBytes + 1;

    await ana.sendBytes(chat);

    expect(server.host.rejections[InputRejection.overBudget], 1);
    expect(server.wire.refused, isEmpty);
  });

  test('random bytes from a client are counted and nothing throws', () async {
    final ana = connect();
    await ana.login('Ana');
    final random = Random(3);
    for (var i = 0; i < 15; i++) {
      await ana.sendBytes(
        Uint8List.fromList([
          for (var j = 0; j < 1 + random.nextInt(40); j++) random.nextInt(256),
        ]),
      );
      server.loop.runTick();
    }

    final counted = server.wire.refused.values.fold(0, (a, b) => a + b);
    expect(counted + server.inbox.pending('ana'), greaterThan(0));
    expect(server.host.clientCount, 1);
  });

  test(
    'a session that ends takes the player\'s waiting intents with it',
    () async {
      final ana = connect();
      await ana.login('Ana');
      await ana.send(Walk(TileCoord(1, 1, 0)));
      expect(server.inbox.pending('ana'), 1);

      await server.host.removeClient('ana');

      expect(server.inbox.pending('ana'), 0);
    },
  );
}
