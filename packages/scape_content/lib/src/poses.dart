import 'dart:math' show pi, sin;

import 'package:scape_core/scape_core.dart';

/// How a rig is turned at one moment, in radians. The arms and legs swing
/// forward when positive, [lean] bends the body forward, and [lie] is how far
/// the actor has fallen onto its back, from 0 upright to 1 flat.
final class Pose {
  const Pose({
    this.legL = 0,
    this.legR = 0,
    this.armL = 0,
    this.armR = 0,
    this.lean = 0,
    this.lie = 0,
  });

  final double legL;
  final double legR;
  final double armL;
  final double armR;
  final double lean;
  final double lie;
}

/// Seconds an act takes. A resting state has no length.
double? actSeconds(ActorState state) => switch (state) {
  ActorState.attack => 0.5,
  ActorState.hurt => 0.4,
  ActorState.death => 1.0,
  ActorState.gather => 0.8,
  _ => null,
};

/// The pose of [state] after [seconds]. A resting state repeats and an act
/// plays once, then holds its last pose.
// shortcut: poses come from sine curves, not authored clips. Swap `poseOf`
// for a clip sampler when the drawing app has art.
Pose poseOf(ActorState state, double seconds) => switch (state) {
  ActorState.idle => _idle(seconds),
  ActorState.walk => _stride(seconds, rate: 1.8, reach: 0.6),
  ActorState.run => _stride(seconds, rate: 2.8, reach: 1.0, lean: 0.2),
  ActorState.attack => _swing(_progress(state, seconds)),
  ActorState.hurt => _recoil(_progress(state, seconds)),
  ActorState.death => _fall(_progress(state, seconds)),
  ActorState.gather => _work(_progress(state, seconds)),
};

// The `!` is safe because every state that reaches here is an act.
double _progress(ActorState act, double seconds) =>
    (seconds / actSeconds(act)!).clamp(0.0, 1.0).toDouble();

Pose _idle(double seconds) {
  final sway = 0.04 * sin(seconds * 2);
  return Pose(armL: sway, armR: -sway);
}

Pose _stride(
  double seconds, {
  required double rate,
  required double reach,
  double lean = 0,
}) {
  final leg = reach * sin(seconds * rate * 2 * pi);
  return Pose(
    legL: leg,
    legR: -leg,
    armL: -leg * 0.8,
    armR: leg * 0.8,
    lean: lean,
  );
}

Pose _swing(double progress) {
  final arc = sin(progress * pi);
  return Pose(armR: -2.2 * arc, lean: 0.25 * arc);
}

Pose _recoil(double progress) {
  final arc = sin(progress * pi);
  return Pose(armL: -0.6 * arc, armR: -0.6 * arc, lean: -0.35 * arc);
}

Pose _fall(double progress) {
  final left = 1 - progress;
  return Pose(lie: 1 - left * left);
}

Pose _work(double progress) {
  final arc = sin(progress * pi);
  return Pose(armL: -1.2 * arc, armR: -1.2 * arc, lean: 0.35);
}
