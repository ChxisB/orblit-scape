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

/// Dice for a fight. Every hit roll answers [hit]. A damage roll answers its
/// top value, so a hit does the most it can.
final class _Dice implements Random {
  double hit = 0;

  @override
  double nextDouble() => hit;

  @override
  bool nextBool() => throw UnimplementedError();

  @override
  int nextInt(int max) => max - 1;
}

const int _bolts = 100;
const int _crossbow = 101;
const int _archer = 3;
const int _mage = 4;
const int _brute = 5;

/// The default items and a crossbow that fires bolts, which the bow cannot.
final ItemSet _items = ItemSet(
  skills: defaultSkills,
  items: [
    ...defaultItems.items,
    ItemDef(
      id: _bolts,
      name: 'Bolts',
      stackable: true,
      equipSlot: 'ammo',
      combat: CombatBonus(ammo: 'bolt'),
    ),
    ItemDef(
      id: _crossbow,
      name: 'Crossbow',
      equipSlot: 'weapon',
      combat: CombatBonus(
        speed: 5,
        style: CombatStyle.ranged,
        range: 7,
        ammo: 'bolt',
      ),
    ),
  ],
);

/// Shots that fly a tile a tick, so a long shot is in the air a long while.
CombatRules _slowShots() => CombatRules(
  fightingSkill: 'fighting',
  healthSkill: 'health',
  projectileSpeed: 1,
);

NpcSet _foes({int health = 30, int respawnTicks = 10}) => NpcSet(
  npcs: [
    NpcDef(
      id: 1,
      name: 'Rat',
      fight: NpcFight(health: health, respawnTicks: respawnTicks),
    ),
    NpcDef(
      id: _archer,
      name: 'Archer',
      fight: NpcFight(health: health, style: CombatStyle.ranged),
    ),
    NpcDef(
      id: _brute,
      name: 'Brute',
      behaviour: Aggressive(sight: 6, leash: 12),
      fight: NpcFight(health: health, strength: 200),
    ),
    NpcDef(
      id: _mage,
      name: 'Mage',
      fight: NpcFight(health: health, style: CombatStyle.magic),
    ),
  ],
);

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  List<Hit> get hits => messages.whereType<Hit>().toList();

  /// The hits that landed on something other than this player.
  List<Hit> get foeHits => [
    for (final hit in hits)
      if (hit.target != avatar) hit,
  ];

  List<Projectile> get shots => messages.whereType<Projectile>().toList();

  PrayerView? get prayer => messages.whereType<PrayerView>().lastOrNull;

  int get avatar => messages.whereType<Avatar>().single.networkId;

  Future<void> attack(int networkId) =>
      send(Interact(kind: InteractKind.npc, id: networkId, option: 0));

  Future<void> cast(int spell, [int target = 0]) =>
      send(Cast(spell: spell, target: target));
}

void main() {
  late ScapeServer server;
  late _Dice dice;
  final clients = <TestClient>[];

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  void start({NpcSet? npcs, CombatRules? rules, PrayerSet? prayers}) {
    dice = _Dice();
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: _items,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: npcs ?? _foes(),
      combat: rules ?? defaultCombat,
      spells: defaultSpells,
      prayers: prayers ?? defaultPrayers,
      random: dice,
    );
  }

  Future<TestClient> join(String name) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login(name);
    await tick();
    return client;
  }

  PlacedNpc place(int npc, TileCoord tile) =>
      server.npcs.spawn(NpcSpawn(npc: npc, tile: tile, radius: 0));

  void wear(String player, int item, [int count = 1]) =>
      server.items.container(player, ContainerId.equipment)!.add(item, count);

  void give(String player, int item, int count) =>
      server.items.container(player, ContainerId.inventory)!.add(item, count);

  void level(String player, String skill, int level) =>
      server.skills.grant(player, skill, defaultSkills.curve.xpForLevel(level));

  int ammoLeft(String player) => server.items
      .container(player, ContainerId.equipment)!
      .countOf(ItemId.bronzeArrows);

  setUp(start);

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  /// Ticks until [client] has [count] of what [of] picks, and returns the
  /// ticks that took. Fails the test if it takes more than [limit].
  Future<int> ticksUntil(
    TestClient client,
    int count,
    List<Object> Function(TestClient) of, {
    int limit = 40,
  }) async {
    for (var ticks = 1; ticks <= limit; ticks++) {
      await tick();
      if (of(client).length >= count) return ticks;
    }
    fail('Nothing arrived in $limit ticks.');
  }

  group('a shot', () {
    for (final distance in [2, 4, 5, 7]) {
      test('over $distance tiles lands on the tick its flight gives', () async {
        final ana = await join('Ana');
        wear('ana', ItemId.shortbow);
        wear('ana', ItemId.bronzeArrows, 10);
        final rat = place(1, at(32 + distance, 32));
        await tick(2);

        await ana.attack(rat.networkId);
        final fired = await ticksUntil(ana, 1, (c) => c.shots);
        final landed = await ticksUntil(ana, 1, (c) => c.hits);

        final expected = defaultCombat.flightTicks(distance);
        expect(ana.shots.first.ticks, expected);
        expect(landed, expected);
        expect(fired, greaterThan(0));
        expect(server.combat.healthOfNpc(rat)!.now, lessThan(30));
      });
    }

    test('from beyond the range closes in first', () async {
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      wear('ana', ItemId.bronzeArrows, 10);
      final rat = place(1, at(44, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await ticksUntil(ana, 1, (c) => c.shots);

      final from = server.movement.tileOf('ana')!;
      expect(44 - from.x, lessThanOrEqualTo(7));
      expect(from.x, greaterThan(32));
    });

    test('spends one arrow each and stops with none left', () async {
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      wear('ana', ItemId.bronzeArrows, 2);
      final rat = place(1, at(35, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(40);

      expect(ana.shots, hasLength(2));
      expect(ammoLeft('ana'), 0);
      expect(ana.notices, ['You have no ammo.']);
    });

    test('is not fired by a bow with no arrows', () async {
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      final rat = place(1, at(35, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(8);

      expect(ana.shots, isEmpty);
      expect(ana.notices, ['You have no ammo.']);
    });

    test('is not fired with ammo of another kind', () async {
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      wear('ana', _bolts, 5);
      final rat = place(1, at(35, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(8);

      expect(ana.shots, isEmpty);
      expect(
        server.items.container('ana', ContainerId.equipment)!.countOf(_bolts),
        5,
      );
    });

    test('whose target dies or is replaced in flight does no harm', () async {
      // A slow shot flies 7 ticks, so the second leaves before the first lands.
      start(npcs: _foes(health: 1, respawnTicks: 1), rules: _slowShots());
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      wear('ana', ItemId.bronzeArrows, 10);
      final rat = place(1, at(39, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await tick(30);

      expect(ana.shots, hasLength(2));
      expect(ana.hits, hasLength(1));
      expect(server.combat.healthOfNpc(rat)!.now, 1);
    });

    test('whose shooter dies in the meantime lands on nobody', () async {
      start(rules: _slowShots());
      final ana = await join('Ana');
      wear('ana', ItemId.shortbow);
      wear('ana', ItemId.bronzeArrows, 10);
      final rat = place(1, at(39, 32));
      await tick(2);
      await ana.attack(rat.networkId);
      await ticksUntil(ana, 1, (c) => c.shots);

      place(_brute, at(33, 32));
      await tick(12);

      expect(ana.notices, contains('Oh dear, you are dead.'));
      expect(server.combat.healthOfNpc(rat)!.now, 30);
    });

    test('counts the arrows as gear only for the bow they suit', () async {
      final ana = await join('Ana');
      wear('ana', ItemId.bronzeArrows, 10);
      wear('ana', ItemId.bronzeSword);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await ticksUntil(ana, 1, (c) => c.foeHits);

      // The sword adds 6 strength and gives a most of 1 + 9 * 70 / 320. The
      // arrows would add 4 and make it 3.
      expect(ana.foeHits.first.damage, 1 + (1 + 8) * (6 + 64) ~/ 320);
    });
  });

  group('the style edge', () {
    for (final (foe, lands) in [(1, true), (_archer, false), (_mage, false)]) {
      test('lets a bow hurt only a creature it beats: $foe', () async {
        dice.hit = 0.55;
        final ana = await join('Ana');
        wear('ana', ItemId.shortbow);
        wear('ana', ItemId.bronzeArrows, 10);
        final target = place(foe, at(34, 32));
        await tick(2);

        await ana.attack(target.networkId);
        await ticksUntil(ana, 1, (c) => c.hits);

        expect(ana.hits.first.damage > 0, lands);
      });
    }

    for (final (foe, lands) in [(_mage, true), (1, false)]) {
      test(
        'lets a creature hurt a bowman only if it beats the bow: $foe',
        () async {
          dice.hit = 0.48;
          final ana = await join('Ana');
          wear('ana', ItemId.shortbow);
          wear('ana', ItemId.bronzeArrows, 10);
          final target = place(foe, at(33, 32));
          await tick(2);

          await ana.attack(target.networkId);
          await tick(10);

          final onAna = ana.hits.where((hit) => hit.target == ana.avatar);
          expect(onAna, isNotEmpty);
          expect(onAna.first.damage > 0, lands);
        },
      );
    }
  });

  group('a spell', () {
    test('costs runes, flies, hurts and gives experience', () async {
      final ana = await join('Ana');
      give('ana', ItemId.runes, 5);
      final rat = place(1, at(35, 32));
      await tick(2);

      await ana.cast(SpellId.strike, rat.networkId);
      await ticksUntil(ana, 1, (c) => c.shots);
      final landed = await ticksUntil(ana, 1, (c) => c.hits);

      expect(ana.shots.single.style, CombatStyle.magic);
      expect(landed, defaultCombat.flightTicks(3));
      expect(server.combat.healthOfNpc(rat)!.now, lessThan(30));
      expect(
        server.items
            .container('ana', ContainerId.inventory)!
            .countOf(ItemId.runes),
        4,
      );
      expect(server.skills.xpOf('ana', 'magic'), 6);
    });

    test('that is refused costs nothing', () async {
      final ana = await join('Ana');
      final rat = place(1, at(35, 32));
      final far = place(1, at(55, 32));
      await tick(2);
      await ana.cast(SpellId.strike, rat.networkId);
      await tick();
      give('ana', ItemId.runes, 5);
      await ana.cast(SpellId.strike, far.networkId);
      await ana.cast(SpellId.daze, rat.networkId);
      await tick();

      expect(ana.notices, [
        'You do not have what that spell costs.',
        'That is out of range.',
        'You need level 5 in magic.',
      ]);
      expect(
        server.items
            .container('ana', ContainerId.inventory)!
            .countOf(ItemId.runes),
        5,
      );
      expect(server.skills.xpOf('ana', 'magic'), 0);
      expect(ana.shots, isEmpty);
    });

    test('waits for the one before it', () async {
      final ana = await join('Ana');
      give('ana', ItemId.runes, 5);
      final rat = place(1, at(35, 32));
      await tick(2);

      await ana.cast(SpellId.strike, rat.networkId);
      await ana.cast(SpellId.strike, rat.networkId);
      await tick();

      expect(ana.notices, ['You are not ready yet.']);
      expect(ana.shots, hasLength(1));
    });

    test('of stunning holds a creature back', () async {
      final ana = await join('Ana');
      level('ana', 'magic', 5);
      give('ana', ItemId.runes, 5);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.cast(SpellId.daze, rat.networkId);
      await tick(5);
      final held = ana.hits.where((hit) => hit.target == ana.avatar);
      await tick(3);
      final freed = ana.hits.where((hit) => hit.target == ana.avatar);

      expect(held, isEmpty);
      expect(freed, isNotEmpty);
    });

    test(
      'of teleporting takes the caster home and ends their attack',
      () async {
        final ana = await join('Ana');
        level('ana', 'magic', 10);
        give('ana', ItemId.runes, 5);
        final rat = place(1, at(45, 32));
        await tick(2);
        await ana.attack(rat.networkId);
        await tick(3);
        expect(server.movement.tileOf('ana')!.x, greaterThan(32));

        await ana.cast(SpellId.home);
        await ana.cast(SpellId.home);
        await tick(12);

        expect(server.movement.tileOf('ana'), spawnTile);
        expect(ana.notices, ['You are not ready yet.']);
        expect(
          server.items
              .container('ana', ContainerId.inventory)!
              .countOf(ItemId.runes),
          2,
        );
      },
    );
  });

  group('a prayer', () {
    test('drains a point in twenty ticks and goes off at zero', () async {
      final ana = await join('Ana');
      expect(ana.prayer!.points, 1);

      await ana.send(TogglePrayer(PrayerId.thickSkin));
      await tick(19);
      expect(ana.prayer!.active, [PrayerId.thickSkin]);
      expect(ana.prayer!.points, 1);
      expect(server.prayers!.percentOf('ana', CombatStat.defence), 10);

      await tick();
      expect(ana.prayer!.points, 0);
      expect(ana.prayer!.active, isEmpty);
      expect(server.prayers!.percentOf('ana', CombatStat.defence), 0);
      expect(ana.notices, ['You have run out of prayer points.']);
    });

    test('tells the player when their prayer level changes', () async {
      final ana = await join('Ana');
      expect(ana.prayer!.max, 1);

      level('ana', 'prayer', 20);
      await tick();

      expect(ana.prayer!.max, 20);
      expect(ana.prayer!.points, 20);
    });

    test('cannot be switched on with no points or too low a level', () async {
      final ana = await join('Ana');
      await ana.send(TogglePrayer(PrayerId.burstOfStrength));
      await tick();
      expect(ana.notices, ['You need level 4 in prayer.']);

      await ana.send(TogglePrayer(PrayerId.thickSkin));
      await tick(20);
      await ana.send(TogglePrayer(PrayerId.thickSkin));
      await tick();

      expect(ana.notices.last, 'You have no prayer points.');
      expect(ana.prayer!.active, isEmpty);
    });

    test('replaces another that raises the same stat', () async {
      final prayers = PrayerSet(
        skill: 'prayer',
        skills: defaultSkills,
        prayers: [
          PrayerDef(
            id: 1,
            name: 'Small',
            level: 1,
            stat: CombatStat.defence,
            percent: 5,
            drain: 5,
          ),
          PrayerDef(
            id: 2,
            name: 'Big',
            level: 1,
            stat: CombatStat.defence,
            percent: 10,
            drain: 8,
          ),
        ],
      );
      await server.dispose();
      start(prayers: prayers);
      final ana = await join('Ana');

      await ana.send(TogglePrayer(1));
      await ana.send(TogglePrayer(2));
      await tick();
      expect(ana.prayer!.active, [2]);

      await ana.send(TogglePrayer(2));
      await tick();
      expect(ana.prayer!.active, isEmpty);
    });

    test('raises the strength a swing is rolled with while it is on', () async {
      final ana = await join('Ana');
      level('ana', 'fighting', 15);
      level('ana', 'prayer', 4);
      final rat = place(1, at(33, 32));
      await tick(2);

      await ana.attack(rat.networkId);
      await ticksUntil(ana, 1, (c) => c.foeHits);
      await ana.send(TogglePrayer(PrayerId.burstOfStrength));
      await ticksUntil(ana, 2, (c) => c.foeHits);

      // Level 15 rolls (15 + 8) * 64 / 320. Ten percent is a level and a
      // half, which rounds up to 17. Cutting it off at 16 would change
      // nothing.
      expect(
        [for (final hit in ana.foeHits) hit.damage],
        [1 + 23 * 64 ~/ 320, 1 + 25 * 64 ~/ 320],
      );
    });
  });
}
