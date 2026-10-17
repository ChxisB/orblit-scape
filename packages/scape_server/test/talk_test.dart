import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
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
  /// The conversation message that came last, or null when none has.
  ServerMessage? get shown => messages
      .where(
        (m) =>
            m is ConversationLine ||
            m is ConversationChoices ||
            m is WindowClosed,
      )
      .lastOrNull;

  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  Future<void> talk(int networkId) => send(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.talk),
  );
}

void main() {
  late ScapeServer server;
  late SaveStore store;
  final servers = <ScapeServer>[];
  final clients = <TestClient>[];

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  ScapeServer build({NpcSet? npcs, Story? story}) {
    final built = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: npcs ?? defaultNpcs,
      shops: defaultShops,
      combat: defaultCombat,
      story: story,
      storage: Storage(
        queue: SaveQueue.inline(
          store,
          clock: () => DateTime.utc(2026),
          onFailure: (_, _) {},
        ),
      ),
    );
    servers.add(built);
    return built;
  }

  /// Starts a server on [store] with the three speakers beside the spawn.
  late PlacedNpc brindle;
  late PlacedNpc guard;
  late PlacedNpc keeper;

  void start() {
    server = build(story: defaultStory);
    brindle = server.npcs.spawn(
      NpcSpawn(npc: NpcId.lamplighter, tile: at(31, 32)),
    );
    guard = server.npcs.spawn(NpcSpawn(npc: NpcId.guard, tile: at(33, 32)));
    keeper = server.npcs.spawn(
      NpcSpawn(npc: NpcId.shopkeeper, tile: at(32, 34)),
    );
  }

  Future<TestClient> join(String name) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await tick(2);
    return client;
  }

  Future<void> say(TestClient ana, PlacedNpc npc) async {
    await ana.talk(npc.networkId);
    await tick(6);
  }

  Future<void> press(TestClient ana, ClientMessage message) async {
    await ana.send(message);
    await tick();
  }

  /// Brindle's ask, the Guard's word and Brindle's thanks.
  Future<void> lightLantern(TestClient ana) async {
    await say(ana, brindle);
    await press(ana, const Continue());
    await press(ana, Choose(0));
    await press(ana, const Continue());
    await say(ana, guard);
    await press(ana, const Continue());
    await say(ana, brindle);
    await press(ana, const Continue());
  }

  setUp(() {
    store = MemorySaveStore();
    start();
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    for (final built in servers) {
      await built.dispose();
    }
    servers.clear();
  });

  group('a quest', () {
    test('starts, advances and completes with its reward', () async {
      final ana = await join('Ana');

      await say(ana, brindle);
      await press(ana, const Continue());
      await press(ana, Choose(0));
      expect(server.quests!.stagesOf('ana'), {'lantern': 1});
      await press(ana, const Continue());
      await say(ana, guard);
      await press(ana, const Continue());
      expect(server.quests!.stagesOf('ana'), {'lantern': 2});
      await say(ana, brindle);

      expect(server.quests!.stagesOf('ana'), {'lantern': 3});
      expect(server.skills.xpOf('ana', 'firemaking'), 80);
      expect(
        server.items
            .container('ana', ContainerId.inventory)!
            .countOf(ItemId.coins),
        25,
      );
      expect(ana.notices, contains('Quest complete: The Dark Lantern'));
    });

    test('gives nothing a second time', () async {
      final ana = await join('Ana');
      await lightLantern(ana);

      await say(ana, brindle);
      await say(ana, guard);

      expect(server.skills.xpOf('ana', 'firemaking'), 80);
      expect(ana.notices.where((n) => n.startsWith('Quest complete')), [
        'Quest complete: The Dark Lantern',
      ]);
    });

    test('advances across a restart and completes with its reward', () async {
      final first = await join('Ana');
      await say(first, brindle);
      await press(first, const Continue());
      await press(first, Choose(0));
      await server.dispose();

      start();
      final ana = await join('Ana');
      await tick(2);
      expect(server.quests!.stagesOf('ana'), {'lantern': 1});
      await say(ana, guard);
      await press(ana, const Continue());
      await say(ana, brindle);

      expect(server.quests!.stagesOf('ana'), {'lantern': 3});
      expect(server.skills.xpOf('ana', 'firemaking'), 80);
    });

    test('puts a reward that does not fit at the player\'s feet', () async {
      final ana = await join('Ana');
      final bag = server.items.container('ana', ContainerId.inventory)!;
      for (var i = 0; i < inventorySize; i++) {
        bag.add(ItemId.bones, 1);
      }

      await lightLantern(ana);

      final here = server.movement.tileOf('ana')!;
      expect(server.ground.lyingAt('ana', here).map((l) => l.item), [
        ItemId.coins,
      ]);
      expect(server.ground.lyingAt('ana', here).single.count, 25);
    });

    test('drops a saved stage the game no longer has', () async {
      await join('Ana');

      final dropped = server.quests!.restore('ana', {'lantern': 2, 'gone': 1});

      expect(dropped, 1);
      expect(server.quests!.stagesOf('ana'), {'lantern': 2});
    });
  });

  group('a conversation', () {
    test('changes with the stage', () async {
      final ana = await join('Ana');
      await say(ana, guard);
      expect((ana.shown as ConversationLine).text, 'Move along.');
      await press(ana, const Continue());

      await say(ana, brindle);
      await press(ana, const Continue());
      await press(ana, Choose(0));
      await press(ana, const Continue());
      await say(ana, guard);

      expect(
        (ana.shown as ConversationLine).text,
        'The tower is safe by day. Tell the lamplighter.',
      );
    });

    test('offers a choice only while its condition holds', () async {
      final ana = await join('Ana');
      await say(ana, keeper);
      await press(ana, const Continue());
      expect((ana.shown as ConversationChoices).options, ['Just looking.']);
      await press(ana, Choose(0));

      await lightLantern(ana);
      await say(ana, keeper);
      await press(ana, const Continue());

      expect((ana.shown as ConversationChoices).options, [
        'Can I help?',
        'Just looking.',
      ]);
    });

    test('refuses a choice that does not exist', () async {
      final ana = await join('Ana');
      await say(ana, brindle);
      await press(ana, const Continue());

      await press(ana, Choose(2));
      await press(ana, const Continue());

      expect(ana.notices, ['That is not on offer.', 'That is not on offer.']);
      expect(server.quests!.stagesOf('ana'), isEmpty);
      expect(ana.shown, isA<ConversationChoices>());
    });

    test('refuses a choice for a line', () async {
      final ana = await join('Ana');
      await say(ana, guard);

      await press(ana, Choose(0));

      expect(ana.notices, ['That is not on offer.']);
    });

    test('ends when the quests changed under it, writing nothing', () async {
      final ana = await join('Ana');
      await say(ana, brindle);
      server.quests!.restore('ana', {'tally': 1});

      await press(ana, const Continue());

      expect(ana.shown, isA<WindowClosed>());
      expect(server.quests!.stagesOf('ana'), {'tally': 1});
    });

    test(
      'says there is nothing to say to a creature with no dialogue',
      () async {
        final ana = await join('Ana');
        final chicken = server.npcs.spawn(
          NpcSpawn(npc: NpcId.chicken, tile: at(33, 33)),
        );

        await say(ana, chicken);

        expect(ana.notices, ['They have nothing to say.']);
        expect(ana.shown, isNull);
      },
    );
  });

  group('starting', () {
    Dialogue parse(String nodes, {String start = 'a'}) => Dialogue.parse('''
{"kind":"orblit.dialogue","formatVersion":1,"key":"d",
 "variants":[{"start":"$start"}],"nodes":{$nodes}}''');

    NpcSet speaker() => NpcSet(
      npcs: [NpcDef(id: 1, name: 'Ann', dialogue: 'd')],
    );

    Story story(Dialogue dialogue) => Story(
      quests: [defaultStory.book.quest('lantern')],
      dialogues: [dialogue],
    );

    test('refuses a creature whose dialogue is not in the story', () {
      final npcs = NpcSet(
        npcs: [NpcDef(id: 1, name: 'Ann', dialogue: 'zz')],
      );

      expect(() => build(npcs: npcs, story: defaultStory), throwsArgumentError);
    });

    test('refuses a dialogue when there is no story', () {
      expect(() => build(npcs: speaker()), throwsArgumentError);
    });

    test('refuses a dialogue that writes a quest stage itself', () {
      final writes = parse(
        '"a":{"type":"set","writes":[{"name":"quest.lantern.stage","value":3}]}',
      );

      expect(
        () => build(npcs: speaker(), story: story(writes)),
        throwsArgumentError,
      );
    });

    test('refuses a line the client cannot show', () {
      final long = 'x' * (maxTextBytes + 1);
      final line = parse('"a":{"type":"line","speaker":"Ann","text":"$long"}');

      expect(
        () => build(npcs: speaker(), story: story(line)),
        throwsArgumentError,
      );
    });

    test('refuses a choice with too many options', () {
      final options = List.generate(
        maxChoices + 1,
        (i) => '{"text":"Pick $i"}',
      ).join(',');
      final many = parse('"a":{"type":"choice","options":[$options]}');

      expect(
        () => build(npcs: speaker(), story: story(many)),
        throwsArgumentError,
      );
    });
  });
}
