import 'package:scape_core/scape_core.dart';

import 'engine/registry.dart';
import 'engine/services.dart';
import 'features/movement/movement.dart';

/// The one place features join the server. Each feature adds its action kinds,
/// systems and components to [registry] here.
void registerFeatures(
  Registry registry,
  Services services, {
  required TileCoord spawn,
}) {
  Movement.register(registry, services, spawn: spawn);
}
