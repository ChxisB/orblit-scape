import 'dart:math' show tan;

import 'package:orblit_terrain/orblit_terrain.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart' hide RegionKey;
import 'package:test/test.dart';
import 'package:vector_math/vector_math_64.dart';

const double width = 800;
const double height = 600;

/// The pixel a world point is drawn at: the inverse of `rayThrough`.
({double x, double y}) pixelOf(OrbitView view, Vector3 point) {
  final look = view.forward;
  final right = look.cross(Vector3(0, 1, 0)).normalized();
  final up = right.cross(look);
  final offset = point - view.position;
  final depth = offset.dot(look);
  final reach = depth * tan(view.fovY / 2);
  final across = offset.dot(right) / (reach * width / height);
  final down = offset.dot(up) / reach;
  return (x: (across + 1) / 2 * width, y: (1 - down) / 2 * height);
}

void main() {
  final slope = Terrain(regionSize: 64)
    ..fillHeights(const RegionKey(0, 0), (x, z) => x * 0.5);

  test('every tile on a slope is picked by the pixel it is drawn at', () {
    final view = OrbitView(
      target: standingPoint(slope, 30, 30),
      yaw: 0.9,
      pitch: 0.8,
      distance: 20,
    );

    for (final (x, y) in [(30, 30), (28, 33), (35, 27), (22, 22), (40, 36)]) {
      final pixel = pixelOf(
        view,
        standingPoint(slope, x.toDouble(), y.toDouble()),
      );
      final ray = view.rayThrough(pixel.x, pixel.y, width, height);

      expect(pickTile(slope, ray), TileCoord(x, y, 0), reason: '($x, $y)');
    }
  });

  test('a ray that rises above the horizon picks nothing', () {
    final view = OrbitView(target: standingPoint(slope, 30, 30), pitch: 0);
    expect(pickTile(slope, view.rayThrough(400, 0, width, height)), isNull);
  });

  test('ground outside the world picks nothing', () {
    final terrain = Terrain(regionSize: 64)
      ..fillHeights(const RegionKey(-1, 0), (x, z) => 0);
    final view = OrbitView(
      target: Vector3(-10, 0, 10),
      pitch: 1.2,
      distance: 10,
    );
    expect(pickTile(terrain, view.rayThrough(400, 300, width, height)), isNull);
  });

  test('the tile keeps the plane it is asked for', () {
    final view = OrbitView(target: standingPoint(slope, 30, 30));
    final ray = view.rayThrough(400, 300, width, height);
    expect(pickTile(slope, ray, plane: 2)!.plane, 2);
  });

  test('an actor stands in the middle of its tile on the ground', () {
    final point = standingPoint(slope, 10, 12);
    expect(point.x, 10.5);
    expect(point.z, 12.5);
    expect(point.y, closeTo(slope.heightAt(10.5, 12.5)!, 1e-12));
  });
}
