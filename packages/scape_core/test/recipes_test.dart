import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

const int fire = 1;
const int anvil = 2;

final ObjectSet testObjects = ObjectSet(
  objects: [
    ObjectDef(id: fire, name: 'Fire'),
    ObjectDef(id: anvil, name: 'Anvil', solid: true),
  ],
);

Recipe recipe({
  String skill = 'fighting',
  int level = 5,
  RecipeStart start = const OnObject(logs, fire),
  List<ItemStack>? inputs,
  Product? product,
  int? tool,
  int ticks = 3,
  Failure? failure,
}) => Recipe(
  name: 'cook the logs',
  skill: skill,
  level: level,
  xp: 10,
  start: start,
  inputs: inputs ?? [ItemStack(logs, 1)],
  product: product ?? Gives(ItemStack(helmet, 1)),
  tool: tool,
  ticks: ticks,
  failure: failure,
);

RecipeSet setOf(List<Recipe> recipes) => RecipeSet(
  recipes: recipes,
  items: testItems(),
  objects: testObjects,
  skills: testSkills,
);

void main() {
  group('Recipe', () {
    test('a failure chance falls in a line to zero at the safe level', () {
      final def = recipe(failure: Failure(chance: 0.5, safeLevel: 15));

      expect(def.failChanceAt(1), 0.5);
      expect(def.failChanceAt(5), 0.5);
      expect(def.failChanceAt(10), closeTo(0.25, 1e-9));
      expect(def.failChanceAt(15), 0);
      expect(def.failChanceAt(50), 0);
    });

    test('a recipe with no failure never fails', () {
      expect(recipe().failChanceAt(1), 0);
    });

    test('rejects a level, ticks or inputs out of range', () {
      expect(() => recipe(level: 0), throwsArgumentError);
      expect(() => recipe(level: maxLevel + 1), throwsArgumentError);
      expect(() => recipe(ticks: 0), throwsArgumentError);
      expect(() => recipe(ticks: maxMakeTicks + 1), throwsArgumentError);
      expect(() => recipe(inputs: []), throwsArgumentError);
    });

    test('rejects experience below one', () {
      expect(
        () => Recipe(
          name: 'cook the logs',
          skill: 'fighting',
          level: 5,
          xp: 0,
          start: const OnObject(logs, fire),
          inputs: [ItemStack(logs, 1)],
          product: Gives(ItemStack(helmet, 1)),
        ),
        throwsArgumentError,
      );
    });

    test('rejects a failure that is safe at or below the level', () {
      expect(
        () => recipe(failure: Failure(chance: 0.5, safeLevel: 5)),
        throwsArgumentError,
      );
    });

    test('rejects a failure chance outside 0 to 1', () {
      expect(() => Failure(chance: 0, safeLevel: 9), throwsArgumentError);
      expect(() => Failure(chance: 1.5, safeLevel: 9), throwsArgumentError);
      expect(() => Failure(chance: 0.5, safeLevel: 1), throwsArgumentError);
    });

    test('a fire needs an object and a lifetime', () {
      expect(() => Lights(object: 0, ticks: 5), throwsArgumentError);
      expect(() => Lights(object: fire, ticks: 0), throwsArgumentError);
    });
  });

  group('OnItem', () {
    test('has the same key in either order', () {
      expect(const OnItem(3, 7).key, const OnItem(7, 3).key);
    });
  });

  group('RecipeSet', () {
    test('accepts recipes whose names all exist', () {
      expect(
        () => setOf([
          recipe(failure: Failure(chance: 0.5, safeLevel: 9, item: arrows)),
          recipe(
            start: const OnItem(sword, logs),
            tool: sword,
            product: Lights(object: fire, ticks: 50),
          ),
        ]),
        returnsNormally,
      );
    });

    test('rejects an item, object or skill that does not exist', () {
      expect(
        () => setOf([
          recipe(inputs: [ItemStack(99, 1)]),
        ]),
        throwsArgumentError,
      );
      expect(
        () => setOf([recipe(start: const OnObject(logs, 9))]),
        throwsArgumentError,
      );
      expect(
        () => setOf([recipe(product: Lights(object: 9, ticks: 5))]),
        throwsArgumentError,
      );
      expect(() => setOf([recipe(skill: 'baking')]), throwsArgumentError);
      expect(
        () => setOf([
          recipe(failure: Failure(chance: 0.5, safeLevel: 9, item: 99)),
        ]),
        throwsArgumentError,
      );
    });

    test('rejects a product that does not exist', () {
      expect(
        () => setOf([recipe(product: Gives(ItemStack(99, 1)))]),
        throwsArgumentError,
      );
    });

    test('rejects a level above the skill cap', () {
      expect(() => setOf([recipe(level: 60)]), throwsArgumentError);
    });

    test('rejects a start that uses an item the recipe does not hold', () {
      expect(
        () => setOf([recipe(start: const OnObject(sword, fire))]),
        throwsArgumentError,
      );
      expect(
        () => setOf([recipe(start: const OnItem(logs, sword))]),
        throwsArgumentError,
      );
    });

    test('a tool counts as held for the start', () {
      expect(
        () => setOf([recipe(start: const OnItem(logs, sword), tool: sword)]),
        returnsNormally,
      );
    });

    test('rejects two recipes that start the same way', () {
      expect(() => setOf([recipe(), recipe()]), throwsArgumentError);
      expect(
        () => setOf([
          recipe(start: const OnItem(logs, sword), tool: sword),
          recipe(start: const OnItem(sword, logs), tool: sword),
        ]),
        throwsArgumentError,
      );
    });

    test('an item pair and an item on an object are different starts', () {
      expect(
        () => setOf([
          recipe(
            start: const OnItem(coins, logs),
            inputs: [ItemStack(coins, 1), ItemStack(logs, 1)],
          ),
          recipe(
            start: const OnObject(coins, anvil),
            inputs: [ItemStack(coins, 1)],
          ),
        ]),
        returnsNormally,
      );
    });

    test('the same item on different stations is two recipes', () {
      expect(
        () => setOf([recipe(), recipe(start: const OnObject(logs, anvil))]),
        returnsNormally,
      );
    });
  });
}
