import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

extension on TestClient {
  List<Said> get heard => messages.whereType<Said>().toList();
  List<Whispered> get whispers => messages.whereType<Whispered>().toList();
  List<String> get notices => [
    for (final n in messages.whereType<Notice>()) n.text,
  ];
  SocialLists get lists => messages.whereType<SocialLists>().last;
  int get avatar => messages.whereType<Avatar>().single.networkId;
}

void main() {
  planeTests();

  late ScapeServer server;
  final clients = <TestClient>[];

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
    await tick();
    return client;
  }

  setUp(() {
    server = ScapeServer(
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
    );
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('public chat', () {
    test(
      'reaches the speaker and a player nearby, naming the speaker',
      () async {
        final ana = await join('Ana');
        final bo = await join('Bo');

        await ana.send(Chat('hello'));
        await tick();

        expect(bo.heard.single.speaker, ana.avatar);
        expect(bo.heard.single.text, 'hello');
        expect(ana.heard.single.speaker, ana.avatar);
      },
    );

    test(
      'stops at the chat radius though the speaker is still in view',
      () async {
        final ana = await join('Ana');
        final bo = await join('Bo');
        await bo.send(Walk(at(52, 32)));
        await tick(40);
        await tick();
        expect(ana.client.entityCount, 2);

        await ana.send(Chat('can you hear me'));
        await tick();

        expect(bo.heard, isEmpty);
        expect(ana.heard, hasLength(1));
      },
    );

    test('is not heard by a player who ignores the speaker', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final cy = await join('Cy');
      await bo.send(EditList(list: ListKind.ignored, add: true, name: 'ana'));

      await ana.send(Chat('hello'));
      await tick();

      expect(bo.heard, isEmpty);
      expect(cy.heard, hasLength(1));
      expect(ana.heard, hasLength(1));
    });

    test('a muted player is told so and nobody hears them', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      server.social.mute('ANA');
      expect(server.social.isMuted('ana'), isTrue);

      await ana.send(Chat('hello'));
      await tick();

      expect(ana.notices, ['You are muted.']);
      expect(bo.heard, isEmpty);
      expect(ana.heard, isEmpty);

      server.social.unmute('ana');
      await ana.send(Chat('hello again'));
      await tick();

      expect(bo.heard, hasLength(1));
    });
  });

  group('whispers', () {
    test('go to the named player only, by any case of their name', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final cy = await join('Cy');

      await ana.send(Whisper(to: 'BO', text: 'psst'));
      await tick();

      expect(bo.whispers.single.from, 'ana');
      expect(bo.whispers.single.text, 'psst');
      expect(cy.whispers, isEmpty);
      expect(ana.whispers, isEmpty);
    });

    test('reach a player far away on the map', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await bo.send(Walk(at(110, 32)));
      await tick(80);

      await ana.send(Whisper(to: 'Bo', text: 'still there'));
      await tick();

      expect(bo.whispers, hasLength(1));
    });

    test('to a name that is offline is answered with a notice', () async {
      final ana = await join('Ana');

      await ana.send(Whisper(to: 'Nobody', text: 'hi'));
      await tick();

      expect(ana.notices, ['That player is not online.']);
    });

    test(
      'to a player who ignores the sender looks the same as offline',
      () async {
        final ana = await join('Ana');
        final bo = await join('Bo');
        await bo.send(EditList(list: ListKind.ignored, add: true, name: 'Ana'));

        await ana.send(Whisper(to: 'Bo', text: 'hi'));
        await tick();

        expect(bo.whispers, isEmpty);
        expect(ana.notices, ['That player is not online.']);
      },
    );

    test('from a muted player are refused', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      server.social.mute('ana');

      await ana.send(Whisper(to: 'Bo', text: 'hi'));
      await tick();

      expect(bo.whispers, isEmpty);
      expect(ana.notices, ['You are muted.']);
    });
  });

  group('lists', () {
    test('a login is sent empty lists', () async {
      final ana = await join('Ana');

      expect(ana.lists.friends, isEmpty);
      expect(ana.lists.ignored, isEmpty);
    });

    test('adding and removing a name sends the list back', () async {
      final ana = await join('Ana');

      await ana.send(EditList(list: ListKind.friends, add: true, name: 'Bo'));
      await ana.send(EditList(list: ListKind.ignored, add: true, name: 'Cy'));
      await tick();
      expect(ana.lists.friends, [(name: 'bo', online: false)]);
      expect(ana.lists.ignored, ['cy']);

      await ana.send(EditList(list: ListKind.friends, add: false, name: 'BO'));
      await tick();
      expect(ana.lists.friends, isEmpty);
    });

    test(
      'a name already on a list changes nothing and sends nothing',
      () async {
        final ana = await join('Ana');
        await ana.send(EditList(list: ListKind.friends, add: true, name: 'Bo'));
        final before = ana.messages.whereType<SocialLists>().length;

        await ana.send(EditList(list: ListKind.friends, add: true, name: 'bo'));
        await ana.send(
          EditList(list: ListKind.friends, add: false, name: 'Cy'),
        );

        expect(ana.messages.whereType<SocialLists>().length, before);
      },
    );

    test('a list stops at fifty names', () async {
      final ana = await join('Ana');
      for (var i = 0; i <= maxListEntries; i++) {
        ana.client.sendMessage(
          encodeClientMessage(
            EditList(list: ListKind.friends, add: true, name: 'n$i'),
          ),
        );
        if (i % 10 == 9) await tick();
      }
      await tick(2);

      expect(ana.lists.friends, hasLength(maxListEntries));
      expect(ana.notices, ['That list is full.']);
    });

    test('a player cannot add themselves', () async {
      final ana = await join('Ana');

      await ana.send(EditList(list: ListKind.ignored, add: true, name: 'ANA'));
      await tick();

      expect(ana.lists.ignored, isEmpty);
      expect(ana.notices, ['You cannot add yourself.']);
    });

    test('show a friend come online and go', () async {
      final ana = await join('Ana');
      await ana.send(EditList(list: ListKind.friends, add: true, name: 'Bo'));
      await tick();

      final bo = await join('Bo');
      expect(ana.lists.friends, [(name: 'bo', online: true)]);

      await bo.dispose();
      clients.remove(bo);
      await tick(15);
      expect(ana.lists.friends, [(name: 'bo', online: false)]);
    });

    test(
      'a player is sent the lists they left with when they return',
      () async {
        final ana = await join('Ana');
        await ana.send(EditList(list: ListKind.friends, add: true, name: 'Bo'));
        await ana.dispose();
        clients.remove(ana);
        await tick(15);

        final again = await join('Ana');

        expect(again.lists.friends, [(name: 'bo', online: false)]);
      },
    );
  });
}

final class _Recorder implements Hosting {
  final Map<String, List<ServerMessage>> told = {};

  @override
  int spawn(int entity, {String? owner}) => 0;

  @override
  void despawn(int entity) {}

  @override
  void tell(String player, ServerMessage message) =>
      (told[player] ??= []).add(message);
}

void planeTests() {
  test('chat does not cross planes at the same tile', () {
    final standing = {
      'ana': (networkId: 1, tile: TileCoord(5, 5, 0)),
      'bo': (networkId: 2, tile: TileCoord(5, 5, 1)),
      'cy': (networkId: 3, tile: TileCoord(5, 5, 0)),
    };
    final hosting = _Recorder();
    final registry = Registry();
    Social.register(
      registry,
      Services(
        world: World(),
        map: openWorld(),
        hosting: hosting,
        active: ActiveCells(
          grid: Grid(16),
          radius: 1,
          hold: Duration.zero,
          clock: () => Duration.zero,
        ),
      ),
      standing: (player) => standing[player],
    );
    for (final name in standing.keys) {
      registry.playerJoined(name);
    }

    registry.deliver('ana', Chat('hello'));

    expect(hosting.told['bo']?.whereType<Said>(), isEmpty);
    expect(hosting.told['cy']!.whereType<Said>(), hasLength(1));
  });
}
