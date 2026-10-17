import 'dart:math';

import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/bank/bank.dart';
import 'features/combat/combat.dart';
import 'features/gathering/gathering.dart';
import 'features/interactions/interactions.dart';
import 'features/items/ground.dart';
import 'features/items/items.dart';
import 'features/loot/loot.dart';
import 'features/movement/movement.dart';
import 'features/npcs/npcs.dart';
import 'features/objects/objects.dart';
import 'features/production/production.dart';
import 'features/shops/shops.dart';
import 'features/skills/skills.dart';
import 'features/social/social.dart';
import 'features/windows/windows.dart';

/// What the rest of the server may reach into after the features register.
typedef Features = ({
  Combat combat,
  GroundItems ground,
  Interactions interactions,
  Items items,
  Npcs npcs,
  WorldObjects objects,
  Skills skills,
  Social social,
});

/// The one place features join the server. Each feature adds its action kinds,
/// systems and components to [registry] here.
Features registerFeatures(
  Registry registry,
  Services services, {
  required TileCoord spawn,
  required SkillSet skills,
  required ItemSet items,
  required ObjectSet objects,
  required NodeSet nodes,
  required NpcSet npcs,
  required CombatRules combat,
  required Random random,
  RecipeSet? recipes,
  ShopRules? shops,
}) {
  final movement = Movement.register(registry, services, spawn: spawn);
  final social = Social.register(
    registry,
    services,
    standing: movement.standing,
  );
  final bags = Items.register(registry, services, items: items);
  final ground = GroundItems.register(
    registry,
    services,
    items: items,
    tileOf: movement.tileOf,
    inventoryOf: (player) => bags.container(player, ContainerId.inventory),
  );
  final creatures = Npcs.register(
    registry,
    services,
    npcs: npcs,
    playerTiles: () => movement.tiles,
    random: random,
  );
  final placed = WorldObjects.register(registry, services, objects: objects);
  final levels = Skills.register(registry, services, skills: skills);
  final interactions = Interactions.register(
    registry,
    services,
    movement: movement,
    objects: placed,
    npcs: creatures,
    ground: ground,
    items: bags,
  );
  Gathering.register(
    registry,
    services,
    interactions: interactions,
    nodes: nodes,
    items: items,
    objects: placed,
    skills: levels,
    bags: bags,
    actorOf: movement.actorOf,
    random: random,
  );
  if (recipes != null) {
    Production.register(
      registry,
      services,
      interactions: interactions,
      recipes: recipes,
      items: items,
      objects: placed,
      skills: levels,
      bags: bags,
      actorOf: movement.actorOf,
      tileOf: movement.tileOf,
      random: random,
    );
  }
  final fights = Combat.register(
    registry,
    services,
    interactions: interactions,
    rules: combat,
    skills: levels,
    items: items,
    bags: bags,
    movement: movement,
    npcs: creatures,
    random: random,
    spawn: spawn,
  );
  registerLoot(
    combat: fights,
    ground: ground,
    items: items,
    npcs: npcs,
    random: random,
  );
  final windows = Windows.register(registry, services, movement: movement);
  Bank.register(
    registry,
    services,
    interactions: interactions,
    bags: bags,
    windows: windows,
  );
  if (shops != null) {
    Shops.register(
      registry,
      services,
      rules: shops,
      items: items,
      npcs: npcs,
      interactions: interactions,
      bags: bags,
      windows: windows,
    );
  } else if (npcs.npcs.any((npc) => npc.shop != null)) {
    throw ArgumentError.value(shops, 'shops', 'is needed for a creature shop');
  }
  return (
    combat: fights,
    ground: ground,
    interactions: interactions,
    items: bags,
    npcs: creatures,
    objects: placed,
    skills: levels,
    social: social,
  );
}
