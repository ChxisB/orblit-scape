import 'dart:math';

import 'package:vector_math/vector_math_64.dart';

const double minPitch = 0.15;
const double maxPitch = 1.45;
const double minDistance = 4;
const double maxDistance = 40;

/// A ray from the camera through one pixel.
typedef PickRay = ({Vector3 origin, Vector3 direction});

/// A camera circling a point, looking at it. Input makes a new view, so the
/// one on screen never changes under a pick that is half done.
final class OrbitView {
  OrbitView({
    required this.target,
    this.yaw = 0,
    double pitch = 0.9,
    double distance = 14,
    this.fovY = 0.8,
  }) : pitch = pitch.clamp(minPitch, maxPitch),
       distance = distance.clamp(minDistance, maxDistance) {
    if (!fovY.isFinite || fovY <= 0 || fovY >= pi) {
      throw ArgumentError.value(fovY, 'fovY', 'is not between 0 and pi');
    }
    if (!target.storage.every((v) => v.isFinite) || !yaw.isFinite) {
      throw ArgumentError('A view needs a finite target and yaw.');
    }
  }

  /// The point looked at. Never changed after the view is made.
  final Vector3 target;

  /// Turn about the vertical axis, in radians.
  final double yaw;

  /// Angle above the ground, in radians.
  final double pitch;

  final double distance;

  /// Vertical field of view, in radians.
  final double fovY;

  Vector3 get position =>
      target +
      Vector3(sin(yaw) * cos(pitch), sin(pitch), cos(yaw) * cos(pitch)) *
          distance;

  /// The unit direction the camera looks along.
  Vector3 get forward => (target - position).normalized();

  OrbitView turned(double byYaw, double byPitch) => OrbitView(
    target: target,
    yaw: yaw + byYaw,
    pitch: pitch + byPitch,
    distance: distance,
    fovY: fovY,
  );

  /// Moves in by [factor] below one and out above it.
  OrbitView zoomed(double factor) => OrbitView(
    target: target,
    yaw: yaw,
    pitch: pitch,
    distance: distance * factor,
    fovY: fovY,
  );

  OrbitView following(Vector3 point) => OrbitView(
    target: point,
    yaw: yaw,
    pitch: pitch,
    distance: distance,
    fovY: fovY,
  );

  /// The ray through the pixel ([x], [y]) of a viewport [width] by [height],
  /// measured from its top left.
  PickRay rayThrough(double x, double y, double width, double height) {
    final look = forward;
    final right = look.cross(Vector3(0, 1, 0)).normalized();
    final up = right.cross(look);
    final reach = tan(fovY / 2);
    final across = (2 * x / width - 1) * reach * (width / height);
    final down = (1 - 2 * y / height) * reach;
    return (
      origin: position,
      direction: (look + right * across + up * down).normalized(),
    );
  }
}
