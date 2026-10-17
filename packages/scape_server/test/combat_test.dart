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

/// Dice for a fight. A swing draws a hit roll and, on a hit, a damage roll.
/// [hit] answers the first. [damage] answers the second, held under the most
/// the roll allows.
final class _Dice implements Random {
  _Dice({required this.hit, this.damage = 0});

  double hit;
  final int damage;
  int drawn = 0;

  @override
  double nextDouble() {
    drawn++;
    return hit;
  }

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => min(damage, max - 1);
}

/// Every swing lands for one.
_Dice _alwaysOne() => _Dice(hit: 0);

/// Every swing misses.
_Dice _neverHits() => _Dice(hit: 0.99);

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  List<Hit> get hits => messages.whereType<Hit>().toList();

  int get avatar => messages.whereType<Avatar>().single.networkId;

  Future<void> attack(int networkId) =>
      send(Interact(kind: InteractKind.npc, id: networkId, option: 0));
}

/// A creature that is easy to read: one hit point per [health], swinging every
/// [speed] ticks for up to its strength.
NpcSet _foes({
  int health = 3,
  int speed = 4,
  int defence = 1,
  int respawnTicks = 10,
  Behaviour behaviour = const Idle(),
}) => NpcSet(
  npcs: [
    NpcDef(
      id: 1,
      name: 'Rat',
      behaviour: behaviour,
      fight: NpcFight(
        health: health,
        speed: speed,
        defence: defence,
        respawnTicks: respawnTicks,
      ),
    ),
    NpcDef(id: 2, name: 'Post'),
  ],
);

/// The default items and two with bonuses far past anything in the content,
/// so a test can tell that gear counts.
final ItemSet _gearItems = ItemSet(
  skills: defaultSkills,
  items: [
    ...defaultItems.items,
    ItemDef(
      id: _mace,
      name: 'Mace',
      equipSlot: 'weapon',
      combat: CombatBonus(attack: 300, strength: 600, speed: 4),
    ),
    ItemDef(
      id: _plate,
      name: 'Plate',
      equipSlot: 'body',
      combat: CombatBonus(defence: 600),
    ),
  ],
);

const int _mace = 100;
const int _plate = 101;

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

  void start({NpcSet? npcs, Random? random, CombatRules? rules}) {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: _gearItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: npcs ?? _foes(),
      combat: rules ?? defaultCombat,
      random: random ?? _alwaysOne(),
    );
  }

  PlacedNpc place(int npc, TileCoord tile, {int radius = 0}) =>
      server.npcs.spawn(NpcSpawn(npc: npc, tile: tile, radius: radius));

  void wear(String player, int item) =>
      server.items.container(player, ContainerId.equipment)!.add(item, 1);

  setUp(() => start());

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  group('attacking', () {
    test('a click walks up to the creature and swings', () async {
      final ana = await join('Ana');
      final rat = place(1, at(36, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(8);

      expect(ana.hits, isNotEmpty);
      expect(ana.hits.first.target, rat.networkId);
      expect(server.combat.healthOfNpc(rat)?.now, lessThan(3));
    });

    test('a creature with no fight cannot be attacked', () async {
      final ana = await join('Ana');
      final post = place(2, at(33, 32));
      await tick(2);

      await ana.attack(post.networkId);
      await tick(4);

      expect(ana.notices, ['You cannot attack that.']);
      expect(ana.hits, isEmpty);
    });

    test('is a fight for the combat ticks after a swing', () async {
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      expect(server.combat.isFighting('ana'), isFalse);

      await ana.attack(rat.networkId);
      await tick(2);
      expect(server.combat.isFighting('ana'), isTrue);

      await tick(40);
      expect(server.combat.isFighting('ana'), isFalse);
      expect(server.combat.isFighting('nobody'), isFalse);
    });

    test('a player who drops in a fight stays in the world longer', () async {
      start(random: _neverHits());
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(2);

      await ana.link.close();
      await tick(30);
      expect(server.combat.isFighting('ana'), isTrue);
      expect(server.movement.tileOf('ana'), isNotNull);

      await tick(80);
      expect(server.movement.tileOf('ana'), isNull);
    });

    test('a click on nothing does nothing', () async {
      final ana = await join('Ana');

      await ana.attack(9999);
      await tick(4);

      expect(ana.hits, isEmpty);
      expect(ana.notices, isEmpty);
    });

    test('swings come at the weapon speed', () async {
      start(random: _neverHits());
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(20);

      final unarmed = ana.hits.where((hit) => hit.target == rat.networkId);
      // The first swing lands on the click tick, then one every four ticks.
      expect(unarmed.length, inInclusiveRange(5, 6));
    });

    test('a sword swings faster than bare hands', () async {
      start(random: _neverHits());
      final ana = await join('Ana');
      server.items
          .container('ana', ContainerId.equipment)!
          .add(ItemId.bronzeSword, 1);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(20);

      final swings = ana.hits.where((hit) => hit.target == rat.networkId);
      final speed = defaultItems.require(ItemId.bronzeSword).combat!.speed!;
      expect(speed, lessThan(defaultCombat.unarmedSpeed + 1));
      expect(swings.length, inInclusiveRange(20 ~/ speed, 20 ~/ speed + 2));
    });

    test('a weapon attack bonus turns a miss into a hit', () async {
      // Bare hands have an even chance, which 0.52 misses. The mace beats it.
      start(random: _Dice(hit: 0.52));
      final ana = await join('Ana');
      wear('ana', _mace);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(1);

      expect(ana.hits.first.damage, greaterThan(0));
    });

    test('bare hands miss the same roll', () async {
      start(random: _Dice(hit: 0.52));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(1);

      expect(ana.hits.first.damage, 0);
    });

    test('strength bonus raises the most a hit does', () async {
      start(random: _Dice(hit: 0, damage: 99));
      final ana = await join('Ana');
      wear('ana', _mace);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(1);

      final most = defaultCombat.maxHit((level: 1, bonus: 600));
      expect(ana.hits.first.damage, most);
    });

    test('a creature with a high defence is missed more', () async {
      // 0.3 beats an even chance but not one against defence 50.
      start(npcs: _foes(health: 50, defence: 50), random: _Dice(hit: 0.3));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(12);

      final swings = ana.hits.where((hit) => hit.target == rat.networkId);
      expect(swings, isNotEmpty);
      expect(swings.map((hit) => hit.damage).toSet(), {0});
    });

    test('an attack follows a creature that moves', () async {
      // A wandering rat leaves the tile the click walked towards.
      start(
        npcs: _foes(health: 50, behaviour: const Wander()),
        random: _Dice(hit: 0),
      );
      final ana = await join('Ana');
      final rat = place(1, at(36, 32), radius: 6);
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(40);

      expect(ana.hits.where((hit) => hit.target == rat.networkId), isNotEmpty);
      expect(ana.notices, isNot(contains('You cannot reach that.')));
    });
  });

  group('winning', () {
    test('the creature goes and the kill handler runs once', () async {
      start(npcs: _foes(respawnTicks: 500));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      final kills = <(String, PlacedNpc)>[];
      server.combat.addKillHandler((killer, npc) => kills.add((killer, npc)));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(40);

      expect(kills, [('ana', rat)]);
      expect(rat.isDead, isTrue);
      expect(ana.npcs, isNot(contains(rat.networkId)));
      expect(ana.notices, contains('You defeated the rat.'));
    });

    test('experience is granted once', () async {
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(40);

      expect(
        server.skills.xpOf('ana', 'fighting'),
        3 * defaultCombat.fightingXpPerHealth,
      );
      expect(
        server.skills.xpOf('ana', 'health'),
        3 * defaultCombat.healthXpPerHealth,
      );
    });

    test('two blows on one tick kill it once', () async {
      final ana = await join('Ana');
      final bob = await join('Bob');
      final rat = place(1, at(33, 32));
      var kills = 0;
      server.combat.addKillHandler((_, _) => kills++);
      server.skills.grant('bob', 'fighting', 100000);
      await tick(2);

      await ana.attack(rat.networkId);
      await bob.attack(rat.networkId);
      await tick(40);

      expect(kills, 1);
      expect(
        server.skills.xpOf('ana', 'fighting') +
            server.skills.xpOf('bob', 'fighting'),
        greaterThanOrEqualTo(3 * defaultCombat.fightingXpPerHealth),
      );
    });

    test('it stands again at home after its respawn time', () async {
      start(npcs: _foes(respawnTicks: 60));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(20);
      expect(rat.isDead, isTrue);

      await tick(60);

      expect(rat.isDead, isFalse);
      expect(rat.tile, at(33, 32));
      expect(server.combat.healthOfNpc(rat)?.now, 3);
    });

    test('a creature far from every player stays down', () async {
      start(npcs: _foes(respawnTicks: 60));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(20);
      await ana.send(Walk(at(120, 120)));
      await tick(160);

      expect(rat.isDead, isTrue);
    });

    test('a new health level raises the most hit points', () async {
      // The creature lands its one hit and the new level heals one point.
      start(npcs: _foes(speed: 100));
      final ana = await join('Ana');
      final before = ana.healthOf(ana.avatar)!;
      final rat = place(1, at(33, 32));
      server.skills.grant(
        'ana',
        'health',
        defaultSkills.curve.xpForLevel(2) - 1,
      );
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(20);

      final after = server.combat.healthOfPlayer('ana')!;
      expect(after, (now: before.now, max: before.max + 1));
      expect(ana.healthOf(ana.avatar), after);
    });
  });

  group('being hit', () {
    test('a creature swings back at its own speed', () async {
      start(
        npcs: _foes(health: 50, speed: 3),
        random: _Dice(hit: 0, damage: 0),
      );
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(13);

      final received = ana.hits.where((hit) => hit.target == ana.avatar);
      expect(received.length, inInclusiveRange(3, 5));
    });

    test('the damage shows in the health the client reads', () async {
      start(npcs: _foes(health: 50), random: _Dice(hit: 0, damage: 1));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(6);

      final mine = ana.healthOf(ana.avatar)!;
      expect(mine.now, lessThan(mine.max));
      expect(mine.now, server.combat.healthOfPlayer('ana')!.now);
      expect(ana.healthOf(rat.networkId)!.now, lessThan(50));
    });

    test('a hit goes to the fighter and not to a bystander', () async {
      start(npcs: _foes(health: 50));
      final ana = await join('Ana');
      final bob = await join('Bob');
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(8);

      expect(ana.hits, isNotEmpty);
      expect(bob.hits, isEmpty);
    });

    test('an aggressive creature adopts a player who stands by it', () async {
      start(
        npcs: _foes(health: 50, behaviour: Aggressive(sight: 5, leash: 12)),
      );
      final ana = await join('Ana');
      final rat = place(1, at(36, 32));
      await ana.send(Walk(at(34, 32)));
      await tick(20);

      expect(ana.hits.where((hit) => hit.target == ana.avatar), isNotEmpty);
      expect(rat.isEngaged, isTrue);
    });

    test('armour turns hits into misses', () async {
      // 0.3 beats an even chance but not one against 600 of defence.
      start(npcs: _foes(health: 50), random: _Dice(hit: 0.3));
      final ana = await join('Ana');
      wear('ana', _plate);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(14);

      final taken = ana.hits.where((hit) => hit.target == ana.avatar);
      expect(taken, isNotEmpty);
      expect(taken.map((hit) => hit.damage).toSet(), {0});
    });

    test('a creature that is hit stands and fights', () async {
      // Rolls of zero would send an idle wanderer off every tick.
      final dice = _Dice(hit: 0.99);
      start(
        npcs: _foes(health: 50, behaviour: const Wander()),
        random: dice,
      );
      final ana = await join('Ana');
      final rat = place(1, at(33, 32), radius: 5);
      await tick(2);

      dice.hit = 0;
      await ana.attack(rat.networkId);
      await tick(8);

      expect(rat.tile, at(33, 32));
      expect(rat.isEngaged, isTrue);
    });

    test('a creature lets go of a player who leaves', () async {
      start(npcs: _foes(health: 50));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(3);
      expect(rat.isEngaged, isTrue);

      await server.host.removeClient('ana');
      await tick(2);

      expect(rat.isEngaged, isFalse);
    });

    test('walking away ends the fight', () async {
      start(npcs: _foes(health: 50));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(6);

      await ana.send(Walk(at(45, 45)));
      await tick(30);
      final seen = ana.hits.length;
      await tick(20);

      expect(ana.hits.length, seen);
      expect(rat.isEngaged, isFalse);
    });
  });

  group('dying', () {
    NpcSet deadly() => _foes(health: 500, speed: 1);

    Future<TestClient> loseOnce({int ratX = 38}) async {
      start(npcs: deadly(), random: _Dice(hit: 0, damage: 99));
      final ana = await join('Ana');
      server.items.container('ana', ContainerId.inventory)!.add(ItemId.logs, 1);
      final rat = place(1, at(ratX, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(25);
      return ana;
    }

    test('the player is back at the spawn tile with full health', () async {
      final ana = await loseOnce();

      final health = server.combat.healthOfPlayer('ana')!;
      expect(health.now, health.max);
      expect(ana.healthOf(ana.avatar)!.now, health.max);
      expect(ana.tileOf(ana.avatar), spawnTile);
      expect(ana.notices, contains('Oh dear, you are dead.'));
    });

    test('the player keeps what they carried', () async {
      await loseOnce();

      final bag = server.items.container('ana', ContainerId.inventory)!;
      expect(bag.countOf(ItemId.logs), 1);
    });

    test('a creature by the spawn tile lets the dead go', () async {
      final ana = await loseOnce(ratX: 33);
      final seen = ana.hits.length;

      await tick(20);

      expect(ana.hits.length, seen);
    });

    test('the creature stops fighting a player who died', () async {
      final ana = await loseOnce();
      final seen = ana.hits.length;

      await tick(20);

      expect(ana.hits.length, seen);
    });
  });

  group('healing', () {
    test('a hurt player regains a point only after the fight', () async {
      start(npcs: _foes(health: 50), random: _Dice(hit: 0, damage: 1));
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(6);
      await ana.send(Walk(at(50, 50)));
      await tick(30);
      final hurt = server.combat.healthOfPlayer('ana')!.now;

      await tick(defaultCombat.regenTicks * 2);

      expect(hurt, lessThan(server.combat.healthOfPlayer('ana')!.max));
      expect(server.combat.healthOfPlayer('ana')!.now, greaterThan(hurt));
    });

    test('no point comes back during a fight', () async {
      start(
        npcs: _foes(health: 5000, speed: 20),
        random: _Dice(hit: 0, damage: 0),
      );
      final ana = await join('Ana');
      final rat = place(1, at(33, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await tick(defaultCombat.regenTicks + 5);

      final taken = ana.hits
          .where((hit) => hit.target == ana.avatar)
          .fold(0, (sum, hit) => sum + hit.damage);
      final health = server.combat.healthOfPlayer('ana')!;
      expect(taken, greaterThan(0));
      expect(health.now, health.max - taken);
    });
  });

  test('rules that name a missing skill fail at startup', () async {
    await server.dispose();
    final broken = CombatRules(fightingSkill: 'swords', healthSkill: 'health');

    expect(() => start(rules: broken), throwsArgumentError);
  });
}
