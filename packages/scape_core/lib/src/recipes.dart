import 'items.dart';
import 'objects.dart';
import 'skills.dart';

/// The most ticks one make may take.
const int maxMakeTicks = 100;

/// What a player does to begin a recipe.
sealed class RecipeStart {
  const RecipeStart();
}

/// [item] used on [target], in either order. Both are in the inventory.
final class OnItem extends RecipeStart {
  const OnItem(this.item, this.target);

  final int item;
  final int target;

  /// The same value for either order of the two items.
  (int, int) get key => item < target ? (item, target) : (target, item);
}

/// [item] used on a placed object, the recipe's station.
final class OnObject extends RecipeStart {
  const OnObject(this.item, this.object);

  final int item;

  /// The id of the [ObjectDef] the item is used on.
  final int object;
}

/// What a make gives.
sealed class Product {
  const Product();
}

/// One stack in the inventory.
final class Gives extends Product {
  const Gives(this.stack);

  final ItemStack stack;
}

/// A fire that stands where the player does for [ticks] ticks. [object] is
/// the id of its [ObjectDef].
final class Lights extends Product {
  Lights({required this.object, required this.ticks}) {
    if (object < 1 || object > maxObjectId) {
      throw ArgumentError.value(object, 'object', 'is not an object id');
    }
    if (ticks < 1) throw ArgumentError.value(ticks, 'ticks', 'is below one');
  }

  final int object;
  final int ticks;
}

/// What a failed make costs. The inputs are used up and the player gets
/// [item], or nothing when it is null. The chance is [chance] at the
/// recipe's level and falls in a line to zero at [safeLevel].
final class Failure {
  Failure({required this.chance, required this.safeLevel, this.item}) {
    if (chance.isNaN || chance <= 0 || chance > 1) {
      throw ArgumentError.value(chance, 'chance', 'is outside 0 to 1');
    }
    if (safeLevel < 2 || safeLevel > maxLevel) {
      throw ArgumentError.value(
        safeLevel,
        'safeLevel',
        'is outside 2 to $maxLevel',
      );
    }
  }

  final double chance;
  final int safeLevel;
  final int? item;
}

/// One thing a player can make: the inputs it uses up, the tool it needs in
/// hand, the skill and level it asks for, and what it gives.
final class Recipe {
  Recipe({
    required this.name,
    required this.skill,
    required this.level,
    required this.xp,
    required this.start,
    required List<ItemStack> inputs,
    required this.product,
    this.tool,
    this.ticks = 3,
    this.failure,
  }) : inputs = List.unmodifiable(inputs) {
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (level < 1 || level > maxLevel) {
      throw ArgumentError.value(level, 'level', 'is outside 1 to $maxLevel');
    }
    if (xp < 1) throw ArgumentError.value(xp, 'xp', 'is below one');
    if (inputs.isEmpty) throw ArgumentError.value(inputs, 'inputs', 'is empty');
    if (ticks < 1 || ticks > maxMakeTicks) {
      throw ArgumentError.value(
        ticks,
        'ticks',
        'is outside 1 to $maxMakeTicks',
      );
    }
    if (failure != null && failure!.safeLevel <= level) {
      throw ArgumentError.value(failure, 'failure', 'is safe below its level');
    }
  }

  /// A verb phrase that finishes "You fail to": `cook the fish`.
  final String name;
  final String skill;
  final int level;

  /// Experience for each make that works.
  final int xp;
  final RecipeStart start;
  final List<ItemStack> inputs;
  final Product product;

  /// An item held or worn and not used up, or null.
  final int? tool;

  /// Ticks for each make.
  final int ticks;
  final Failure? failure;

  /// The chance that one make fails for a player of [skillLevel].
  double failChanceAt(int skillLevel) {
    final failure = this.failure;
    if (failure == null || skillLevel >= failure.safeLevel) return 0;
    if (skillLevel <= level) return failure.chance;
    final along = (skillLevel - level) / (failure.safeLevel - level);
    return failure.chance * (1 - along);
  }
}

/// The recipes a game has. Each must name items, an object and a skill that
/// exist and a level the skill reaches, so a typo fails when the game starts
/// and not when a player tries. A start names one recipe, so a second recipe
/// for the same two items would never run.
final class RecipeSet {
  RecipeSet({
    required List<Recipe> recipes,
    required ItemSet items,
    required ObjectSet objects,
    required SkillSet skills,
  }) : recipes = List.unmodifiable(recipes) {
    final starts = <Object>{};
    for (final recipe in recipes) {
      _check(recipe, items, objects, skills);
      if (!starts.add(_startKey(recipe.start))) {
        throw ArgumentError.value(recipe.name, 'recipes', 'repeats a start');
      }
    }
  }

  final List<Recipe> recipes;
}

Object _startKey(RecipeStart start) => switch (start) {
  OnItem(:final key) => key,
  OnObject(:final item, :final object) => ('object', item, object),
};

void _check(Recipe recipe, ItemSet items, ObjectSet objects, SkillSet skills) {
  final skill = skills.find(recipe.skill);
  if (skill == null) {
    throw ArgumentError.value(recipe.skill, recipe.name, 'skill is unknown');
  }
  if (recipe.level > skill.levelCap) {
    throw ArgumentError.value(recipe.level, recipe.name, 'is above the cap');
  }
  final held = {for (final stack in recipe.inputs) stack.item, ?recipe.tool};
  for (final id in [
    ...held,
    ...switch (recipe.product) {
      Gives(:final stack) => [stack.item],
      Lights() => const <int>[],
    },
    ?recipe.failure?.item,
  ]) {
    items.require(id);
  }
  switch (recipe.start) {
    case OnItem(:final item, :final target):
      _checkHeld(recipe, [item, target], held);
    case OnObject(:final item, :final object):
      _checkHeld(recipe, [item], held);
      objects.require(object);
  }
  if (recipe.product case Lights(:final object)) objects.require(object);
}

void _checkHeld(Recipe recipe, List<int> used, Set<int> held) {
  if (!held.containsAll(used)) {
    throw ArgumentError.value(
      recipe.name,
      'start',
      'uses an item that is neither an input nor the tool',
    );
  }
}
