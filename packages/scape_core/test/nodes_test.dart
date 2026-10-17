import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

NodeDef node({
  int object = 1,
  String skill = 'fighting',
  int level = 5,
  int tool = sword,
  int xp = 10,
  int product = logs,
  double chanceLow = 0.2,
  double chanceHigh = 0.8,
  int fullLevel = 15,
  double depleteChance = 0.1,
  int respawnTicks = 10,
}) => NodeDef(
  object: object,
  skill: skill,
  level: level,
  tool: tool,
  xp: xp,
  product: product,
  chanceLow: chanceLow,
  chanceHigh: chanceHigh,
  fullLevel: fullLevel,
  depleteChance: depleteChance,
  respawnTicks: respawnTicks,
);

final ObjectSet testObjects = ObjectSet(
  objects: [
    ObjectDef(id: 1, name: 'Tree', solid: true),
    ObjectDef(id: 2, name: 'Rock', solid: true),
  ],
);

void main() {
  group('NodeDef', () {
    test('success chance rises in a line from level to full level', () {
      final def = node();

      expect(def.chanceAt(1), 0.2);
      expect(def.chanceAt(5), 0.2);
      expect(def.chanceAt(10), closeTo(0.5, 1e-9));
      expect(def.chanceAt(15), 0.8);
      expect(def.chanceAt(50), 0.8);
    });

    test('rejects a level outside range or a full level not above it', () {
      expect(() => node(level: 0), throwsArgumentError);
      expect(() => node(level: maxLevel + 1), throwsArgumentError);
      expect(() => node(fullLevel: 5), throwsArgumentError);
      expect(() => node(fullLevel: maxLevel + 1), throwsArgumentError);
    });

    test('rejects a chance outside 0 to 1 and a high below the low', () {
      expect(() => node(chanceLow: -0.1), throwsArgumentError);
      expect(() => node(chanceHigh: 1.1), throwsArgumentError);
      expect(() => node(depleteChance: 2), throwsArgumentError);
      expect(() => node(depleteChance: double.nan), throwsArgumentError);
      expect(() => node(chanceLow: 0.6, chanceHigh: 0.5), throwsArgumentError);
      expect(node(chanceLow: 1, chanceHigh: 1).chanceAt(5), 1);
    });

    test('rejects no experience and no respawn', () {
      expect(() => node(xp: 0), throwsArgumentError);
      expect(() => node(respawnTicks: 0), throwsArgumentError);
    });
  });

  group('NodeSet', () {
    NodeSet build(List<NodeDef> nodes) => NodeSet(
      nodes: nodes,
      objects: testObjects,
      items: testItems(),
      skills: testSkills,
    );

    test('finds a node by the object it lives on', () {
      final set = build([node(), node(object: 2)]);

      expect(set.find(2)!.object, 2);
      expect(set.find(3), isNull);
    });

    test('rejects a repeated object', () {
      expect(() => build([node(), node()]), throwsArgumentError);
    });

    test('rejects a missing object, tool, product or skill', () {
      expect(() => build([node(object: 9)]), throwsArgumentError);
      expect(() => build([node(tool: 99)]), throwsArgumentError);
      expect(() => build([node(product: 99)]), throwsArgumentError);
      expect(() => build([node(skill: 'baking')]), throwsArgumentError);
    });
  });
}
