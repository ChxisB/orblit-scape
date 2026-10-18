import 'dart:math';

import 'package:scape_core/scape_core.dart';

import '../../engine/act.dart';
import '../../engine/action.dart';
import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../objects/objects.dart';
import '../skills/skills.dart';

/// Cooking, smithing, fletching and lighting fires. A player who uses one
/// item on another, or on a station, keeps making what the recipe names
/// every [Recipe.ticks] until they run out of inputs, the make is refused or
/// they do something else. A lit fire is a placed object that goes out after
/// the ticks its recipe names.
final class Production {
  Production._(
    this._registry,
    this._services, {
    required ItemSet items,
    required WorldObjects objects,
    required Skills skills,
    required Items bags,
    required int? Function(String) actorOf,
    required TileCoord? Function(String) tileOf,
    required Random random,
    required Act act,
  }) : _items = items,
       _objects = objects,
       _skills = skills,
       _bags = bags,
       _actorOf = actorOf,
       _tileOf = tileOf,
       _random = random,
       _act = act;

  /// Adds the recipes of [recipes] to [registry]. Each is a use on an item or
  /// an object, so [interactions] decides when it starts.
  static void register(
    Registry registry,
    Services services, {
    required Interactions interactions,
    required RecipeSet recipes,
    required ItemSet items,
    required WorldObjects objects,
    required Skills skills,
    required Items bags,
    required int? Function(String) actorOf,
    required TileCoord? Function(String) tileOf,
    required Random random,
    required Act act,
  }) {
    final feature = Production._(
      registry,
      services,
      items: items,
      objects: objects,
      skills: skills,
      bags: bags,
      actorOf: actorOf,
      tileOf: tileOf,
      random: random,
      act: act,
    );
    for (final recipe in recipes.recipes) {
      feature._listen(interactions, recipe);
    }
    for (final ticks in {for (final r in recipes.recipes) r.ticks}) {
      final kind = ActionKind('make$ticks', delay: ticks);
      registry.addKind(kind);
      feature._kinds[ticks] = kind;
    }
    registry.addSystem(Phase.npcs, feature._burnOut);
  }

  final Registry _registry;
  final Services _services;
  final ItemSet _items;
  final WorldObjects _objects;
  final Skills _skills;
  final Items _bags;
  final int? Function(String) _actorOf;
  final TileCoord? Function(String) _tileOf;
  final Random _random;
  final Act _act;

  /// One action kind for each number of ticks a make can take, since a kind
  /// has one delay.
  final Map<int, ActionKind> _kinds = {};

  /// The tick each lit fire goes out.
  final Map<PlacedObject, int> _goesOutAt = {};

  void _listen(Interactions interactions, Recipe recipe) {
    switch (recipe.start) {
      case OnItem(:final item, :final target):
        interactions.addUseOnItem(
          item,
          target,
          (player, _, _) => _begin(player, recipe, null),
        );
      case OnObject(:final item, :final object):
        interactions.addUseOnObject(
          item,
          object,
          (player, _, station) => _begin(player, recipe, station),
        );
    }
  }

  void _notice(String player, String text) =>
      _services.hosting.tell(player, Notice(text));

  void _begin(String player, Recipe recipe, PlacedObject? station) {
    final refusal = _refusal(player, recipe, station);
    if (refusal != null) {
      _notice(player, refusal);
      return;
    }
    _queue(player, recipe, station);
  }

  void _queue(String player, Recipe recipe, PlacedObject? station) {
    final actor = _actorOf(player);
    if (actor == null) return;
    _registry.actions.submit(
      actor,
      _kinds[recipe.ticks]!, // register made a kind for every recipe's ticks
      () => _make(player, recipe, station),
    );
  }

  void _make(String player, Recipe recipe, PlacedObject? station) {
    final actor = _actorOf(player);
    if (actor == null) return;
    final refusal = _refusal(player, recipe, station);
    if (refusal != null) {
      _notice(player, refusal);
      return;
    }
    _act(actor, ActorState.make);
    final level = _skills.levelOf(player, recipe.skill);
    final failed = _random.nextDouble() < recipe.failChanceAt(level);
    final done = failed ? _fail(player, recipe) : _succeed(player, recipe);
    if (done && _refusal(player, recipe, station) == null) {
      _queue(player, recipe, station);
    }
  }

  /// Uses the inputs and gives the failure's item. Returns whether it did.
  bool _fail(String player, Recipe recipe) {
    final item = recipe.failure?.item;
    final outcome = _bag(player).exchange(
      take: recipe.inputs,
      give: [if (item != null) ItemStack(item, 1)],
    );
    if (outcome != Outcome.done) {
      _notice(player, 'You have no room for that.');
      return false;
    }
    _notice(player, 'You fail to ${recipe.name}.');
    return true;
  }

  /// Uses the inputs, gives the product and experience. Returns whether it
  /// did.
  bool _succeed(String player, Recipe recipe) {
    final product = recipe.product;
    final outcome = _bag(player).exchange(
      take: recipe.inputs,
      give: [if (product case Gives(:final stack)) stack],
    );
    if (outcome != Outcome.done) {
      _notice(player, 'You have no room for that.');
      return false;
    }
    if (product case Lights(:final object, :final ticks)) {
      _light(player, object, ticks);
    }
    _skills.award(player, recipe.skill, recipe.xp);
    return true;
  }

  void _light(String player, int object, int ticks) {
    final fire = _objects.place(object, _tileOf(player)!); // _refusal read it
    _goesOutAt[fire] = _registry.actions.tick + ticks;
  }

  /// Why [player] cannot make [recipe] now, or null when they can.
  String? _refusal(String player, Recipe recipe, PlacedObject? station) {
    final tile = _tileOf(player);
    final tool = recipe.tool;
    if (tile == null) return 'You cannot do that.';
    if (station != null && _objects.find(station.networkId) == null) {
      return 'The ${station.def.name.toLowerCase()} has gone.';
    }
    if (_skills.levelOf(player, recipe.skill) < recipe.level) {
      return 'You need level ${recipe.level} in ${recipe.skill} to do that.';
    }
    if (tool != null && !_holds(player, tool)) {
      return 'You need a ${_items.require(tool).name.toLowerCase()} to do that.';
    }
    if (!_hasInputs(player, recipe)) return 'You do not have what you need.';
    if (recipe.product case Lights(
      :final object,
    ) when !_objects.canPlace(object, tile)) {
      return 'You cannot light a fire here.';
    }
    return null;
  }

  bool _hasInputs(String player, Recipe recipe) => recipe.inputs.every(
    (stack) => _bag(player).countOf(stack.item) >= stack.count,
  );

  Container _bag(String player) =>
      _bags.container(player, ContainerId.inventory)!; // every player has one

  bool _holds(String player, int tool) => [
    ContainerId.inventory,
    ContainerId.equipment,
  ].any((id) => (_bags.container(player, id)?.countOf(tool) ?? 0) > 0);

  void _burnOut() {
    final tick = _registry.actions.tick;
    for (final MapEntry(key: fire, value: due) in _goesOutAt.entries.toList()) {
      if (due > tick) continue;
      _objects.remove(fire);
      _goesOutAt.remove(fire);
    }
  }
}
