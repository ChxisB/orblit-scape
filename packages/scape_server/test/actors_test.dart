import 'dart:math';

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

/// Dice for a fight: every hit roll answers [hit], and damage is [damage]
/// held under the most the roll allows.
final class _Dice implements Random {
  _Dice({required this.hit, this.damage = 0});

  final double hit;
  final int damage;

  @override
  double nextDouble() => hit;

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => min(damage, max - 1);
}

extension on TestClient {
  int get avatar => messages.whereType<Avatar>().single.networkId;

  Future<void> attack(int networkId) =>
      send(Interact(kind: InteractKind.npc, id: networkId, option: 0));
}

NpcSet _foes({int health = 3, int speed = 4, int respawnTicks = 20}) => NpcSet(
  npcs: [
    NpcDef(
      id: 1,
      name: 'Rat',
      fight: NpcFight(
        health: health,
        speed: speed,
        defence: 1,
        respawnTicks: respawnTicks,
      ),
    ),
  ],
);

void main() {
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

  void start({Random? random, NpcSet? npcs}) {
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
      npcs: npcs ?? _foes(),
      combat: defaultCombat,
      random: random ?? _Dice(hit: 0, damage: 1),
    );
  }

  /// What [viewer] sees [networkId] doing after each of the next [ticks]
  /// ticks.
  Future<List<Anim>> watch(TestClient viewer, int networkId, int ticks) async {
    final seen = <Anim>[];
    for (var i = 0; i < ticks; i++) {
      await tick();
      final anim = viewer.animOf(networkId);
      if (anim != null) seen.add(anim);
    }
    return seen;
  }

  setUp(() => start());

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('look', () {
    test('a new player is seen bare, in colours their name picks', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      await tick();

      final seen = bo.lookOf(ana.avatar)!;
      expect(seen.worn.every((item) => item == 0), isTrue);
      expect(seen, ana.lookOf(ana.avatar));
    });

    test('is the same colours at the next login', () async {
      final ana = await join('Ana');
      final first = ana.lookOf(ana.avatar)!;
      await ana.link.close();
      await tick(12);

      final again = await join('Ana');

      final next = again.lookOf(again.avatar)!;
      expect(
        [next.skin, next.hair, next.cloth],
        [first.skin, first.hair, first.cloth],
      );
    });

    test('names can pick different colours', () async {
      final looks = <Look>[];
      for (final name in ['Ana', 'Bo', 'Cy', 'Di', 'Ed', 'Flo']) {
        final client = await join(name);
        looks.add(client.lookOf(client.avatar)!);
      }

      final cloths = {for (final look in looks) look.cloth};
      expect(cloths.length, greaterThan(1));
    });

    test('shows a helmet another player puts on', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      final head = equipmentSlots.indexOf('head');
      server.items
          .container('ana', ContainerId.inventory)!
          .add(ItemId.bronzeHelmet, 1);
      await tick();

      await ana.send(
        MoveItem(
          from: ContainerId.inventory,
          fromSlot: 0,
          to: ContainerId.equipment,
          toSlot: head,
        ),
      );
      await tick(2);

      expect(bo.lookOf(ana.avatar)!.worn[head], ItemId.bronzeHelmet);
      expect(bo.lookOf(bo.avatar)!.worn[head], 0);
    });

    test('drops the helmet when it comes off', () async {
      final ana = await join('Ana');
      final head = equipmentSlots.indexOf('head');
      final worn = server.items.container('ana', ContainerId.equipment)!
        ..add(ItemId.bronzeHelmet, 1);
      await tick(2);
      expect(ana.lookOf(ana.avatar)!.worn[head], ItemId.bronzeHelmet);

      worn.take(head);
      await tick(2);

      expect(ana.lookOf(ana.avatar)!.worn[head], 0);
    });
  });

  group('resting state', () {
    test('is idle for a player who stands', () async {
      final ana = await join('Ana');
      await tick(2);

      expect(ana.animOf(ana.avatar), Anim());
    });

    test('is walk while they take one step a tick, then idle', () async {
      final ana = await join('Ana');
      await ana.send(Walk(at(34, 32)));
      await tick();
      expect(ana.animOf(ana.avatar)!.base, ActorState.walk);

      await tick(4);

      expect(ana.animOf(ana.avatar)!.base, ActorState.idle);
    });
  });

  group('acts', () {
    test('an attack plays attack for the other player', () async {
      start(npcs: _foes(health: 50, speed: 1000));
      final ana = await join('Ana');
      final bo = await join('Bo');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);

      await ana.attack(rat.networkId);

      final seen = await watch(bo, ana.avatar, 6);
      expect(seen.map((anim) => anim.act), contains(ActorState.attack));
    });

    test('each swing is a new count of the same act', () async {
      start(npcs: _foes(health: 50, speed: 1000));
      final ana = await join('Ana');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);

      await ana.attack(rat.networkId);

      final seen = await watch(ana, ana.avatar, 10);
      final swings = {
        for (final anim in seen)
          if (anim.act == ActorState.attack) anim.seq,
      };
      expect(swings.length, greaterThan(1));
    });

    test('a hit makes the creature hurt', () async {
      start(npcs: _foes(health: 50, speed: 1000));
      final ana = await join('Ana');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);

      await ana.attack(rat.networkId);

      final seen = await watch(ana, rat.networkId, 10);
      expect(seen.map((anim) => anim.act), contains(ActorState.hurt));
    });

    test('chopping a tree plays gather', () async {
      final ana = await join('Ana');
      server.items
          .container('ana', ContainerId.inventory)!
          .add(ItemId.bronzeAxe, 1);
      final tree = server.objects.place(ObjectId.tree, at(33, 32));

      await ana.send(
        Interact(kind: InteractKind.object, id: tree.networkId, option: 0),
      );

      final seen = await watch(ana, ana.avatar, gatherTicks + 2);
      expect(seen.map((anim) => anim.act), contains(ActorState.gather));
    });

    test('a click on a tree with no axe plays nothing', () async {
      final ana = await join('Ana');
      final tree = server.objects.place(ObjectId.tree, at(33, 32));

      await ana.send(
        Interact(kind: InteractKind.object, id: tree.networkId, option: 0),
      );

      final seen = await watch(ana, ana.avatar, gatherTicks + 2);
      expect(seen.map((anim) => anim.act), everyElement(isNull));
    });

    test('a miss does not make the creature hurt', () async {
      start(random: _Dice(hit: 0.99), npcs: _foes(health: 50));
      final ana = await join('Ana');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick();

      expect(ana.animOf(rat.networkId)!.act, isNot(ActorState.hurt));
    });

    test('a creature that dies plays death, lingers, then goes', () async {
      start(random: _Dice(hit: 0, damage: 99), npcs: _foes(health: 1));
      final ana = await join('Ana');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick();
      expect(ana.animOf(rat.networkId)!.act, ActorState.death);

      await tick(deathTicks - 1);
      expect(ana.animOf(rat.networkId)?.act, ActorState.death);

      await tick(2);
      expect(ana.animOf(rat.networkId), isNull);
    });

    test('a dying creature cannot be attacked again', () async {
      start(random: _Dice(hit: 0, damage: 99), npcs: _foes(health: 1));
      final ana = await join('Ana');
      final rat = server.npcs.spawn(NpcSpawn(npc: 1, tile: at(33, 32)));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick();
      final swings = ana.messages.whereType<Hit>().length;

      await ana.attack(rat.networkId);
      await tick(2);

      expect(ana.messages.whereType<Hit>().length, swings);
    });
  });
}
