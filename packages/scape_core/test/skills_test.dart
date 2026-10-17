import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  final curve = XpCurve(quadratic: 30, cubic: 4);

  group('SkillDef', () {
    test('refuses a bad id, an empty name and a cap out of range', () {
      for (final id in ['', 'Mining', '1mine', 'a b', 'a' * 17]) {
        expect(
          () => SkillDef(id: id, name: 'X', levelCap: 10),
          throwsArgumentError,
        );
      }
      expect(
        () => SkillDef(id: 'mining', name: '', levelCap: 10),
        throwsArgumentError,
      );
      expect(
        () => SkillDef(id: 'mining', name: 'M', levelCap: 0),
        throwsArgumentError,
      );
      expect(
        () => SkillDef(id: 'mining', name: 'M', levelCap: maxLevel + 1),
        throwsArgumentError,
      );
    });
  });

  group('XpCurve', () {
    test('level 1 costs nothing and every level costs more than the last', () {
      expect(curve.xpForLevel(1), 0);
      for (var level = 2; level <= maxLevel; level++) {
        expect(
          curve.xpForLevel(level),
          greaterThan(curve.xpForLevel(level - 1)),
        );
      }
    });

    test('a level is reached at its cost and not one point before', () {
      for (var level = 2; level <= 50; level++) {
        final cost = curve.xpForLevel(level);
        expect(curve.levelOf(cost, 50), level);
        expect(curve.levelOf(cost - 1, 50), level - 1);
      }
    });

    test('level never falls as experience rises, and the cap holds', () {
      var last = 1;
      for (var xp = 0; xp < 700000; xp += 37) {
        final level = curve.levelOf(xp, 50);
        expect(level, greaterThanOrEqualTo(last));
        expect(level, lessThanOrEqualTo(50));
        last = level;
      }
      expect(curve.levelOf(0xFFFFFFFF, 50), 50);
      expect(curve.levelOf(-5, 50), 1);
      expect(curve.levelOf(1000000, 1), 1);
    });

    test('refuses a curve with no quadratic term or a negative cubic one', () {
      expect(() => XpCurve(quadratic: 0, cubic: 1), throwsArgumentError);
      expect(() => XpCurve(quadratic: 1, cubic: -1), throwsArgumentError);
    });
  });

  group('SkillSet', () {
    SkillDef def(String id, [int cap = 50]) =>
        SkillDef(id: id, name: id, levelCap: cap);

    test('names each skill in the schema and finds it by id', () {
      final set = SkillSet(
        curve: curve,
        skills: [def('mining'), def('fishing')],
      );

      expect(set.schema.slotCount, 2);
      expect(set.schema.has('skill.mining.xp'), isTrue);
      expect(set.find('fishing')!.id, 'fishing');
      expect(set.find('nothing'), isNull);
    });

    test('refuses a skill listed twice', () {
      expect(
        () => SkillSet(curve: curve, skills: [def('mining'), def('mining')]),
        throwsArgumentError,
      );
    });

    test('refuses a cap whose experience does not fit 32 bits', () {
      expect(
        () => SkillSet(
          curve: XpCurve(quadratic: 100000, cubic: 100000),
          skills: [def('mining', 120)],
        ),
        throwsArgumentError,
      );
    });

    test('stops experience at the cap level\'s cost', () {
      final set = SkillSet(curve: curve, skills: [def('mining', 10)]);

      expect(set.maxXp(set.find('mining')!), curve.xpForLevel(10));
    });
  });
}
