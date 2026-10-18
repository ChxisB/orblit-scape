import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';

/// Seconds added to an actor's clock for each network id, so a crowd that
/// walks does not step together.
const double _phaseStep = 0.37;

/// The act an actor is playing and the count of acts it was last seen at.
final class _Track {
  _Track(this.seq, this.act, this.since);

  int seq;
  ActorState? act;
  double since;
}

/// Turns what the server says an actor is doing into the pose to draw now.
///
/// A resting state repeats. An act plays once from the frame the count of
/// acts changed, then the actor goes back to its resting state. An actor seen
/// for the first time does not replay an act that is over, but a death still
/// shows so a body does not stand up when it comes into view.
final class ActorAnimator {
  final Map<int, _Track> _tracks = {};

  /// The pose of the actor [id], whose server state is [anim], at [now] in
  /// seconds.
  // F5: sampling is what notices a new act, so it records what it saw.
  Pose sample(int id, Anim anim, double now) {
    final track = _tracks[id] = _seen(id, anim, now);
    final act = track.act;
    if (act != null) {
      final age = now - track.since;
      // The `!` holds because an Anim only keeps states that are acts.
      if (age < actSeconds(act)! || act == ActorState.death) {
        return poseOf(act, age);
      }
    }
    return poseOf(anim.base, now + id * _phaseStep);
  }

  /// Keeps what is known about the actors in [ids] and drops the rest, so an
  /// actor that left view is treated as new when it returns.
  void retain(Iterable<int> ids) {
    final keep = ids.toSet();
    _tracks.removeWhere((id, _) => !keep.contains(id));
  }

  _Track _seen(int id, Anim anim, double now) {
    final known = _tracks[id];
    if (known == null) {
      final dead = anim.act == ActorState.death;
      return _Track(
        anim.seq,
        dead ? anim.act : null,
        dead ? now - actSeconds(ActorState.death)! : now,
      );
    }
    if (known.seq != anim.seq) {
      known
        ..seq = anim.seq
        ..act = anim.act
        ..since = now;
    }
    return known;
  }
}
