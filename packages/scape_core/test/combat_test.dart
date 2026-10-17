import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

CombatRules rules() =>
    CombatRules(fightingSkill: 'fighting', healthSkill: 'health');

Hit hearHit(Hit hit) {
  final decoded = decodeServerMessage(encodeServerMessage(hit));
  return switch (decoded) {
    Understood(:final message) => message as Hit,
    Unreadable(:final why) => fail('unreadable: $why'),
  };
}

void main() {
  group('CombatRules', () {
    const even = (level: 20, bonus: 10);

    test('an even match lands half the time', () {
      expect(rules().hitChance(even, even), 0.5);
    });

    test('a stronger attacker lands more often than a weaker one', () {
      const strong = (level: 40, bonus: 30);
      const weak = (level: 5, bonus: 0);
      expect(rules().hitChance(strong, weak), greaterThan(0.5));
      expect(rules().hitChance(weak, strong), lessThan(0.5));
    });

    test('a chance is held between the floor and the ceiling', () {
      const huge = (level: 1000, bonus: 1000);
      const none = (level: 0, bonus: 0);
      expect(rules().hitChance(huge, none), 0.95);
      expect(rules().hitChance(none, huge), 0.05);
    });

    test('a thousand seeded swings hit at the rate the formula gives', () {
      const attack = (level: 30, bonus: 20);
      const defence = (level: 25, bonus: 5);
      final expected = rules().hitChance(attack, defence);
      final random = Random(7);
      var hits = 0;
      for (var i = 0; i < 10000; i++) {
        final damage = rules().swing(
          random,
          attack: attack,
          strength: attack,
          defence: defence,
        );
        if (damage > 0) hits++;
      }
      expect(hits / 10000, closeTo(expected, 0.01));
    });

    test('a hit does from one up to the maximum, and no more', () {
      const strength = (level: 40, bonus: 30);
      final most = rules().maxHit(strength);
      final random = Random(3);
      final damages = {
        for (var i = 0; i < 5000; i++)
          rules().swing(
            random,
            attack: const (level: 99, bonus: 99),
            strength: strength,
            defence: const (level: 1, bonus: 0),
          ),
      }..remove(0);
      expect(damages.reduce(max), most);
      expect(damages.reduce(min), 1);
    });

    test('the maximum hit rises with level and with gear', () {
      expect(rules().maxHit((level: 1, bonus: 0)), 2);
      expect(rules().maxHit((level: 50, bonus: 0)), 12);
      expect(rules().maxHit((level: 50, bonus: 50)), 21);
    });

    test('whole health is the base plus the health level', () {
      expect(rules().maxHealth(1), 10);
      expect(rules().maxHealth(50), 59);
    });

    test('rejects numbers that cannot make a fight', () {
      CombatRules make({
        String fighting = 'fighting',
        int divisor = 320,
        double min = 0.05,
        double max = 0.95,
        int regen = 100,
      }) => CombatRules(
        fightingSkill: fighting,
        healthSkill: 'health',
        hitDivisor: divisor,
        minHitChance: min,
        maxHitChance: max,
        regenTicks: regen,
      );
      expect(() => make(fighting: ''), throwsArgumentError);
      expect(() => make(divisor: 0), throwsArgumentError);
      expect(() => make(min: 0), throwsArgumentError);
      expect(() => make(min: 0.9, max: 0.5), throwsArgumentError);
      expect(() => make(max: 1), throwsArgumentError);
      expect(() => make(regen: 0), throwsArgumentError);
    });
  });

  group('CombatBonus', () {
    test('has no speed unless it is given one', () {
      expect(CombatBonus(attack: 3).speed, isNull);
      expect(CombatBonus(speed: 4).speed, 4);
    });

    test('rejects a negative bonus and a speed below one', () {
      expect(() => CombatBonus(attack: -1), throwsArgumentError);
      expect(() => CombatBonus(strength: -1), throwsArgumentError);
      expect(() => CombatBonus(defence: -1), throwsArgumentError);
      expect(() => CombatBonus(speed: 0), throwsArgumentError);
    });
  });

  group('NpcFight', () {
    test('rejects health or a level below one, and a speed below one', () {
      expect(() => NpcFight(health: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1, attack: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1, strength: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1, defence: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1, speed: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1, respawnTicks: 0), throwsArgumentError);
      expect(() => NpcFight(health: 1), returnsNormally);
    });

    test('a creature has no fight unless it is given one', () {
      expect(NpcDef(id: 1, name: 'Guard').fight, isNull);
      expect(
        NpcDef(id: 1, name: 'Rat', fight: NpcFight(health: 2)).fight!.health,
        2,
      );
    });
  });

  group('ItemDef combat', () {
    test('only a worn item has a bonus, and only a weapon has a speed', () {
      expect(
        () => ItemDef(id: 1, name: 'Rock', combat: CombatBonus(attack: 1)),
        throwsArgumentError,
      );
      expect(
        () => ItemDef(
          id: 1,
          name: 'Cap',
          equipSlot: 'head',
          combat: CombatBonus(speed: 4),
        ),
        throwsArgumentError,
      );
      expect(
        () => ItemDef(
          id: 1,
          name: 'Cap',
          equipSlot: 'head',
          combat: CombatBonus(defence: 2),
        ),
        returnsNormally,
      );
      expect(
        () => ItemDef(
          id: 1,
          name: 'Sword',
          equipSlot: 'weapon',
          combat: CombatBonus(attack: 2, speed: 4),
        ),
        returnsNormally,
      );
    });
  });

  group('Hit', () {
    test('survives the wire, with the largest values it can hold', () {
      final back = hearHit(Hit(target: 0xFFFFFFFF, damage: 0xFFFF));
      expect(back.target, 0xFFFFFFFF);
      expect(back.damage, 0xFFFF);
      expect(hearHit(Hit(target: 5, damage: 0)).damage, 0);
    });

    test('rejects a target or a damage that does not fit', () {
      expect(() => Hit(target: -1, damage: 0), throwsArgumentError);
      expect(() => Hit(target: 0x100000000, damage: 0), throwsArgumentError);
      expect(() => Hit(target: 0, damage: -1), throwsArgumentError);
      expect(() => Hit(target: 0, damage: 0x10000), throwsArgumentError);
    });

    test('a hit of the wrong length is malformed', () {
      final bytes = encodeServerMessage(Hit(target: 1, damage: 1));
      final short = Uint8List.sublistView(bytes, 0, 6);
      expect(
        decodeServerMessage(short),
        isA<Unreadable<ServerMessage>>().having(
          (u) => u.why,
          'why',
          BadMessage.malformed,
        ),
      );
      final long = Uint8List(8)..setRange(0, 7, bytes);
      expect(
        decodeServerMessage(long),
        isA<Unreadable<ServerMessage>>().having(
          (u) => u.why,
          'why',
          BadMessage.tooLarge,
        ),
      );
    });
  });
}
