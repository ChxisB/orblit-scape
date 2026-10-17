import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/movement/movement.dart';
import 'features/skills/skills.dart';
import 'features/social/social.dart';

/// What the rest of the server may reach into after the features register.
typedef Features = ({Skills skills, Social social});

/// The one place features join the server. Each feature adds its action kinds,
/// systems and components to [registry] here.
Features registerFeatures(
  Registry registry,
  Services services, {
  required TileCoord spawn,
  required SkillSet skills,
}) {
  final movement = Movement.register(registry, services, spawn: spawn);
  final social = Social.register(
    registry,
    services,
    standing: movement.standing,
  );
  return (
    skills: Skills.register(registry, services, skills: skills),
    social: social,
  );
}
