import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/items/ground.dart';
import 'features/items/items.dart';
import 'features/movement/movement.dart';
import 'features/skills/skills.dart';
import 'features/social/social.dart';

/// What the rest of the server may reach into after the features register.
typedef Features = ({
  GroundItems ground,
  Items items,
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
}) {
  final movement = Movement.register(registry, services, spawn: spawn);
  final social = Social.register(
    registry,
    services,
    standing: movement.standing,
  );
  final bags = Items.register(registry, services, items: items);
  return (
    ground: GroundItems.register(
      registry,
      services,
      items: items,
      tileOf: movement.tileOf,
      inventoryOf: (player) => bags.container(player, ContainerId.inventory),
    ),
    items: bags,
    skills: Skills.register(registry, services, skills: skills),
    social: social,
  );
}
