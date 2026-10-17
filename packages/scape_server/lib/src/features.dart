import 'dart:math';

import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/combat/combat.dart';
import 'features/gathering/gathering.dart';
import 'features/interactions/interactions.dart';
import 'features/items/ground.dart';
import 'features/items/items.dart';
import 'features/movement/movement.dart';
import 'features/npcs/npcs.dart';
import 'features/objects/objects.dart';
import 'features/skills/skills.dart';
import 'features/social/social.dart';

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
