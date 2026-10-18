import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  late ActorAnimator animator;

  setUp(() => animator = ActorAnimator());

  group('resting', () {
    test('standing is the idle pose', () {
      final pose = animator.sample(1, Anim(), 5);

      expect(pose.legL, 0);
      expect(pose.lie, 0);
    });

    test('walking swings the legs', () {
      final walking = Anim(base: ActorState.walk);

      final swings = {
        for (var i = 0; i < 20; i++)
          animator.sample(1, walking, i / 10).legL.sign,
      };

      expect(swings, containsAll([-1.0, 1.0]));
    });

    test('two walkers are not in step', () {
      final walking = Anim(base: ActorState.walk);

      final a = animator.sample(1, walking, 0.3);
      final b = animator.sample(2, walking, 0.3);

      expect(a.legL, isNot(b.legL));
    });
  });

  group('acts', () {
    test('a new count plays the act from its start', () {
      animator.sample(1, Anim(), 0);
      final swung = Anim().playing(ActorState.attack);

      final start = animator.sample(1, swung, 1);
      final middle = animator.sample(1, swung, 1.25);

      expect(start.armR, closeTo(0, 1e-9));
      expect(middle.armR, lessThan(-2));
    });

    test('the actor rests again when the act is over', () {
      animator.sample(1, Anim(), 0);
      final swung = Anim().playing(ActorState.attack);
      animator.sample(1, swung, 1);

      final after = animator.sample(
        1,
        swung,
        1 + actSeconds(ActorState.attack)! + 1,
      );

      expect(after.armR.abs(), lessThan(0.05));
    });

    test('the same act again restarts', () {
      animator.sample(1, Anim(), 0);
      final first = Anim().playing(ActorState.attack);
      animator.sample(1, first, 1);
      animator.sample(1, first, 1.25);

      final second = first.playing(ActorState.attack);
      final pose = animator.sample(1, second, 3);

      expect(pose.armR, closeTo(0, 1e-9));
    });

    test('the same count is not played twice', () {
      animator.sample(1, Anim(), 0);
      final swung = Anim().playing(ActorState.attack);
      animator.sample(1, swung, 1);

      final later = animator.sample(1, swung, 1.25);
      final again = animator.sample(1, swung, 1.25);

      expect(again.armR, later.armR);
    });

    test('a new count with no act clears the old one', () {
      animator.sample(1, Anim(act: ActorState.death, seq: 4), 0);

      final pose = animator.sample(1, Anim(), 1);

      expect(pose.lie, 0);
    });

    test('a count that wrapped still plays', () {
      animator.sample(1, Anim(), 0);
      final last = Anim(act: ActorState.attack, seq: maxAnimSeq);
      animator.sample(1, last, 1);

      final wrapped = last.playing(ActorState.hurt);
      expect(wrapped.seq, 0);
      animator.sample(1, wrapped, 2);
      final pose = animator.sample(1, wrapped, 2.2);

      expect(pose.lean, lessThan(0));
    });
  });

  group('first sight', () {
    test('an act that is over does not replay', () {
      final swung = Anim(act: ActorState.attack, seq: 9);

      final pose = animator.sample(1, swung, 0.25);

      expect(pose.armR.abs(), lessThan(0.05));
    });

    test('a death shows as fallen', () {
      final dead = Anim(act: ActorState.death, seq: 9);

      expect(animator.sample(1, dead, 100).lie, 1);
    });

    test('an actor that left view is treated as new', () {
      final dead = Anim(act: ActorState.death, seq: 9);
      animator.sample(1, dead, 0);

      animator.retain([2]);
      final swung = Anim(act: ActorState.attack, seq: 9);
      final pose = animator.sample(1, swung, 5.25);

      expect(pose.armR.abs(), lessThan(0.05));
    });
  });

  test('retaining an actor keeps its act', () {
    animator.sample(1, Anim(), 0);
    final swung = Anim().playing(ActorState.attack);
    animator.sample(1, swung, 1);

    animator.retain([1, 2]);

    expect(animator.sample(1, swung, 1.25).armR, lessThan(-2));
  });

  test('a hundred actors for ten seconds of frames is cheap', () {
    final anims = [
      for (var i = 0; i < 100; i++)
        i.isEven
            ? Anim(base: ActorState.walk)
            : Anim().playing(ActorState.attack),
    ];
    final watch = Stopwatch()..start();

    for (var frame = 0; frame < 600; frame++) {
      for (var id = 0; id < anims.length; id++) {
        animator.sample(id, anims[id], frame / 60);
      }
    }

    // This only catches a mistake of an order of magnitude. It is not a frame
    // budget, which needs a drawing app on a device.
    expect(watch.elapsedMilliseconds, lessThan(2000));
  });
}
