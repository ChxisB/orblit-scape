import 'dart:math';

import 'package:scape_client/scape_client.dart';
import 'package:test/test.dart';
import 'package:vector_math/vector_math_64.dart';

void main() {
  final target = Vector3(20, 3, 30);

  test('the ray through the middle of the view reaches the target', () {
    final view = OrbitView(target: target, yaw: 0.7, pitch: 0.6, distance: 10);

    final ray = view.rayThrough(400, 300, 800, 600);
    final reached = ray.origin + ray.direction * view.distance;

    expect((reached - target).length, lessThan(1e-9));
  });

  test('a pixel to the right looks to the right of the target', () {
    final view = OrbitView(target: target, yaw: 1.1);
    final right = view.forward.cross(Vector3(0, 1, 0));

    final left = view.rayThrough(100, 300, 800, 600).direction;
    final far = view.rayThrough(700, 300, 800, 600).direction;

    expect(far.dot(right), greaterThan(0));
    expect(left.dot(right), lessThan(0));
  });

  test('a pixel higher in the view looks higher', () {
    final view = OrbitView(target: target);
    final top = view.rayThrough(400, 0, 800, 600).direction;
    final bottom = view.rayThrough(400, 600, 800, 600).direction;
    expect(top.y, greaterThan(bottom.y));
  });

  test('turning, zooming and following make a new view', () {
    final view = OrbitView(target: target, yaw: 1, pitch: 0.8, distance: 10);

    final turned = view.turned(0.5, 0.1);
    expect(turned.yaw, closeTo(1.5, 1e-12));
    expect(turned.pitch, closeTo(0.9, 1e-12));
    expect(view.yaw, 1);

    expect(view.zoomed(0.5).distance, closeTo(5, 1e-12));
    final moved = view.following(Vector3(1, 2, 3));
    expect(moved.target, Vector3(1, 2, 3));
    expect(moved.yaw, 1);
    expect(view.target, target);
  });

  test('pitch and distance stop at their limits', () {
    final view = OrbitView(target: target);
    expect(view.turned(0, 10).pitch, maxPitch);
    expect(view.turned(0, -10).pitch, minPitch);
    expect(view.zoomed(100).distance, maxDistance);
    expect(view.zoomed(0.001).distance, minDistance);
  });

  test('a view needs a field of view and a finite place', () {
    expect(() => OrbitView(target: target, fovY: 0), throwsArgumentError);
    expect(() => OrbitView(target: target, fovY: pi), throwsArgumentError);
    expect(
      () => OrbitView(target: Vector3(double.nan, 0, 0)),
      throwsArgumentError,
    );
    expect(
      () => OrbitView(target: target, yaw: double.infinity),
      throwsArgumentError,
    );
  });
}
