import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/movement/movement.dart';
import 'features/social/social.dart';

/// What the rest of the server may reach into after the features register.
typedef Features = ({Social social});

/// The one place features join the server. Each feature adds its action kinds,
/// systems and components to [registry] here.
Features registerFeatures(
  Registry registry,
  Services services, {
  required TileCoord spawn,
}) {
  final movement = Movement.register(registry, services, spawn: spawn);
  final social = Social.register(
    registry,
    services,
    standing: movement.standing,
  );
  return (social: social);
}
