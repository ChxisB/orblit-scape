import 'package:orblit_terrain/orblit_terrain.dart';
import 'package:scape_core/scape_core.dart' show TileCoord, worldSize;
import 'package:vector_math/vector_math_64.dart';

import 'orbit_view.dart';

/// The tile under a ray, or null when it meets no ground or meets ground
/// outside the world. One tile is one unit and tile y runs along world z.
TileCoord? pickTile(Terrain terrain, PickRay ray, {int plane = 0}) {
  final hit = terrain.raycast(ray.origin, ray.direction);
  if (hit == null) return null;
  final x = hit.x.floor();
  final y = hit.z.floor();
  if (x < 0 || y < 0 || x >= worldSize || y >= worldSize) return null;
  return TileCoord(x, y, plane);
}

/// Where an actor at tile position ([x], [y]) is drawn: the middle of its
/// tile, on the ground. The position is not whole between two ticks.
Vector3 standingPoint(Terrain terrain, double x, double y) {
  final worldX = x + 0.5;
  final worldZ = y + 0.5;
  return Vector3(worldX, terrain.heightAt(worldX, worldZ) ?? 0, worldZ);
}
