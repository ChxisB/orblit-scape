import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

CombatRules rules() =>
    CombatRules(fightingSkill: 'fighting', healthSkill: 'health');

SkillSet skills() => SkillSet(
  curve: XpCurve(quadratic: 30, cubic: 4),
  skills: [SkillDef(id: 'magic', name: 'Magic', levelCap: 10)],
);

T hear<T>(Decoded<T> decoded) => switch (decoded) {
  Understood(:final message) => message,
  Unreadable(:final why) => fail('unreadable: $why'),
};

BadMessage? why(List<int> bytes, {bool server = false}) {
  final list = Uint8List.fromList(bytes);
  return switch (server
      ? decodeServerMessage(list)
      : decodeClientMessage(list)) {
    Unreadable(:final why) => why,
    Understood() => null,
  };
}

void main() {
  group('flight', () {
    test('takes a tick for each three tiles, and at least one', () {
      final flight = [for (var d = 0; d <= 10; d++) rules().flightTicks(d)];

      expect(flight, [1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4]);
    });

    test('a shot that moves a tile a tick takes the distance in ticks', () {
      final slow = CombatRules(
        fightingSkill: 'fighting',
        healthSkill: 'health',
        projectileSpeed: 1,
      );

      expect([for (var d = 1; d <= 4; d++) slow.flightTicks(d)], [1, 2, 3, 4]);
    });
  });

  group('style edge', () {
    test('each style beats one and loses to one', () {
      final edge = rules().edge;

      expect(edge(CombatStyle.ranged, CombatStyle.melee), 1.25);
      expect(edge(CombatStyle.melee, CombatStyle.magic), 1.25);
      expect(edge(CombatStyle.magic, CombatStyle.ranged), 1.25);
      expect(edge(CombatStyle.melee, CombatStyle.ranged), 1 / 1.25);
      expect(edge(CombatStyle.melee, CombatStyle.melee), 1);
    });

    test('raises a chance to hit when it is above one', () {
      const even = (level: 20, bonus: 10);

      expect(rules().hitChance(even, even, edge: 1.25), greaterThan(0.5));
      expect(rules().hitChance(even, even, edge: 1 / 1.25), lessThan(0.5));
    });

    test('is at least one, so the edge never favours the loser', () {
      expect(
        () => CombatRules(
          fightingSkill: 'fighting',
          healthSkill: 'health',
          styleEdge: 0.9,
        ),
        throwsArgumentError,
      );
    });
  });

  group('gear', () {
    test('a ranged weapon has a range and an ammo kind', () {
      expect(() => CombatBonus(style: CombatStyle.ranged), throwsArgumentError);
      expect(
        () => CombatBonus(
          style: CombatStyle.ranged,
          speed: 4,
          range: 0,
          ammo: 'arrow',
        ),
        throwsArgumentError,
      );
      expect(
        () => CombatBonus(style: CombatStyle.ranged, range: 7, ammo: 'arrow'),
        throwsArgumentError,
        reason: 'a bow with no speed would never be ready again',
      );
      expect(
        () => CombatBonus(range: 5, speed: 4),
        throwsArgumentError,
        reason: 'a melee weapon does not shoot',
      );
      expect(() => CombatBonus(style: CombatStyle.magic), throwsArgumentError);
    });

    test('a bow goes in the weapon slot and an arrow in the ammo slot', () {
      final bow = CombatBonus(
        style: CombatStyle.ranged,
        range: 7,
        ammo: 'arrow',
        speed: 5,
      );
      final arrow = CombatBonus(strength: 7, ammo: 'arrow');

      expect(equipmentSlots.last, 'ammo');
      ItemDef(id: 1, name: 'Bow', equipSlot: 'weapon', combat: bow);
      ItemDef(
        id: 2,
        name: 'Arrow',
        stackable: true,
        equipSlot: 'ammo',
        combat: arrow,
      );
      expect(
        () => ItemDef(id: 1, name: 'Bow', equipSlot: 'body', combat: bow),
        throwsArgumentError,
      );
      expect(
        () => ItemDef(id: 2, name: 'Arrow', equipSlot: 'head', combat: arrow),
        throwsArgumentError,
      );
    });

    test('a creature fights in melee unless it says otherwise', () {
      expect(NpcFight(health: 3).style, CombatStyle.melee);
    });

    test('a save from before the ammo slot still reads', () {
      final old = Uint8List.fromList(
        '{"version":2,"tile":[1,1,0],"containers":{"equipment":[[3,9,1]]}}'
            .codeUnits,
      );

      final state = PlayerState.decode(old);

      expect(state.containers[ContainerId.equipment], hasLength(11));
    });
  });

  group('spells', () {
    SpellSet set(List<SpellDef> spells) => SpellSet(
      skill: 'magic',
      spells: spells,
      items: testItems(),
      skills: skills(),
    );

    test('are found by id', () {
      final spell = SpellDef(
        id: 1,
        name: 'Bolt',
        level: 1,
        effect: SpellDamage(5),
      );

      expect(set([spell]).find(1), same(spell));
      expect(set([spell]).find(2), isNull);
    });

    test('are checked against the game when it starts', () {
      SpellDef spell(
        int id, {
        int level = 1,
        List<ItemStack> cost = const [],
      }) => SpellDef(
        id: id,
        name: 'S$id',
        level: level,
        cost: cost,
        effect: SpellStun(3),
      );

      expect(() => set([spell(1), spell(1)]), throwsArgumentError);
      expect(() => set([spell(1, level: 11)]), throwsArgumentError);
      expect(
        () => set([
          spell(1, cost: [ItemStack(999, 1)]),
        ]),
        throwsArgumentError,
      );
      expect(
        () => SpellSet(
          skill: 'nope',
          spells: [],
          items: testItems(),
          skills: skills(),
        ),
        throwsArgumentError,
      );
    });

    test('cost each item once and have a real effect', () {
      expect(
        () => SpellDef(
          id: 1,
          name: 'Bolt',
          level: 1,
          effect: SpellDamage(5),
          cost: [ItemStack(2, 1), ItemStack(2, 2)],
        ),
        throwsArgumentError,
      );
      expect(() => SpellDamage(0), throwsArgumentError);
      expect(() => SpellStun(0), throwsArgumentError);
    });
  });

  group('prayers', () {
    PrayerDef prayer(int id, {int level = 1, int drain = 5}) => PrayerDef(
      id: id,
      name: 'P$id',
      level: level,
      stat: CombatStat.attack,
      percent: 10,
      drain: drain,
    );

    test('are checked against the game when it starts', () {
      PrayerSet set(List<PrayerDef> prayers) =>
          PrayerSet(skill: 'magic', prayers: prayers, skills: skills());

      expect(set([prayer(1)]).find(1), isNotNull);
      expect(() => set([prayer(1), prayer(1)]), throwsArgumentError);
      expect(() => set([prayer(1, level: 11)]), throwsArgumentError);
      expect(() => prayer(1, drain: 0), throwsArgumentError);
      expect(
        () => PrayerSet(skill: 'x', prayers: [], skills: skills()),
        throwsArgumentError,
      );
    });
  });

  group('messages', () {
    test('a cast names a spell and a target', () {
      final cast =
          hear(
                decodeClientMessage(
                  encodeClientMessage(Cast(spell: 513, target: 70000)),
                ),
              )
              as Cast;

      expect((cast.spell, cast.target), (513, 70000));
      expect(() => Cast(spell: 0x10000, target: 0), throwsArgumentError);
      expect(why([16, 1, 0]), BadMessage.malformed);
    });

    test('a prayer toggle names a prayer', () {
      final toggle =
          hear(decodeClientMessage(encodeClientMessage(TogglePrayer(7))))
              as TogglePrayer;

      expect(toggle.prayer, 7);
      expect(() => TogglePrayer(0), throwsArgumentError);
      expect(why([17, 0]), BadMessage.malformed);
      expect(why([17]), BadMessage.malformed);
    });

    test('a projectile keeps its ends, its flight and its style', () {
      final sent = Projectile(
        from: 9,
        to: 70000,
        ticks: 3,
        style: CombatStyle.magic,
      );

      final heard =
          hear(decodeServerMessage(encodeServerMessage(sent))) as Projectile;

      expect((heard.from, heard.to, heard.ticks), (9, 70000, 3));
      expect(heard.style, CombatStyle.magic);
      expect(
        () => Projectile(from: 1, to: 2, ticks: 0, style: CombatStyle.ranged),
        throwsArgumentError,
      );
      expect(
        why([15, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3], server: true),
        BadMessage.malformed,
      );
    });

    test('a prayer view keeps the points and the prayers that are on', () {
      final view = PrayerView(points: 12, max: 30, active: [2, 5]);

      final heard =
          hear(decodeServerMessage(encodeServerMessage(view))) as PrayerView;

      expect((heard.points, heard.max), (12, 30));
      expect(heard.active, [2, 5]);
      expect(
        () => PrayerView(points: 31, max: 30, active: []),
        throwsArgumentError,
      );
      expect(
        () => PrayerView(points: 1, max: 30, active: [1, 2, 3, 4]),
        throwsArgumentError,
      );
      expect(
        () => PrayerView(points: 1, max: 30, active: [2, 2]),
        throwsArgumentError,
      );
      expect(why([16, 1, 0, 30, 0, 2, 4], server: true), BadMessage.malformed);
      expect(why([16, 31, 0, 30, 0, 0], server: true), BadMessage.malformed);
    });
  });
}
