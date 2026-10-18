import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('only an act has a length', () {
    for (final state in ActorState.values) {
      expect(actSeconds(state) != null, state.isAct, reason: '$state');
    }
  });

  test('walking swings the legs against each other', () {
    final pose = poseOf(ActorState.walk, 0.1);

    expect(pose.legL, isNot(0));
    expect(pose.legR, -pose.legL);
    expect(pose.armL, isNot(pose.legL.sign * pose.armL.abs()));
  });

  test('running reaches further than walking', () {
    double most(ActorState state) => [
      for (var i = 0; i < 100; i++) poseOf(state, i / 50).legL.abs(),
    ].reduce((a, b) => a > b ? a : b);

    expect(most(ActorState.run), greaterThan(most(ActorState.walk)));
  });

  test('standing still is nearly still', () {
    final pose = poseOf(ActorState.idle, 3);

    expect(pose.legL, 0);
    expect(pose.armL.abs(), lessThan(0.05));
  });

  test('a swing lifts the arm mid way and puts it back at the end', () {
    final length = actSeconds(ActorState.attack)!;

    expect(poseOf(ActorState.attack, 0).armR, closeTo(0, 1e-9));
    expect(poseOf(ActorState.attack, length / 2).armR, lessThan(-2));
    expect(poseOf(ActorState.attack, length).armR, closeTo(0, 1e-9));
  });

  test('making bobs both hands and rests at the ends', () {
    final length = actSeconds(ActorState.make)!;

    expect(poseOf(ActorState.make, 0).armR, closeTo(0, 1e-9));
    expect(poseOf(ActorState.make, length / 4).armR, lessThan(-0.8));
    expect(poseOf(ActorState.make, length).armR, closeTo(0, 1e-9));
  });

  test('a hurt actor leans back', () {
    final length = actSeconds(ActorState.hurt)!;

    expect(poseOf(ActorState.hurt, length / 2).lean, lessThan(0));
  });

  test('death falls and then holds', () {
    final length = actSeconds(ActorState.death)!;

    expect(poseOf(ActorState.death, 0).lie, 0);
    expect(poseOf(ActorState.death, length / 2).lie, inExclusiveRange(0, 1));
    expect(poseOf(ActorState.death, length).lie, 1);
    expect(poseOf(ActorState.death, length * 5).lie, 1);
  });

  test('an act held past its length stays at its last pose', () {
    final length = actSeconds(ActorState.gather)!;

    expect(
      poseOf(ActorState.gather, length * 4).armR,
      closeTo(poseOf(ActorState.gather, length).armR, 1e-9),
    );
  });
}
