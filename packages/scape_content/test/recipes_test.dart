import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

Recipe named(String name) =>
    defaultRecipes.recipes.firstWhere((recipe) => recipe.name == name);

void main() {
  test('the first game has a cooking, firemaking, fletching and smithing', () {
    expect(
      {for (final r in defaultRecipes.recipes) r.skill},
      {'cooking', 'firemaking', 'fletching', 'smithing'},
    );
  });

  test('cooking can fail at level 1 and never from level 30', () {
    final cook = named('cook the fish');

    expect(cook.failChanceAt(1), greaterThan(0));
    expect(cook.failChanceAt(30), 0);
  });

  test('a tool is never an input', () {
    for (final recipe in defaultRecipes.recipes) {
      final tool = recipe.tool;
      if (tool != null) {
        expect(recipe.inputs.map((stack) => stack.item), isNot(contains(tool)));
      }
    }
  });

  test('lighting logs places the fire object', () {
    final light = named('light the logs');

    expect((light.product as Lights).object, ObjectId.fire);
  });
}
