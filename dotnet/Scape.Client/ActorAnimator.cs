using Scape.Content;
using Scape.Core;

namespace Scape.Client;

/// <summary>Turns what the server says an actor is doing into the pose to draw now.</summary>
/// <remarks>
/// A resting state repeats. An act plays once from the frame the count of acts changed, then the
/// actor goes back to its resting state. An actor seen for the first time does not replay an act
/// that is over, but a death still shows, so a body does not stand up when it comes into view.
/// </remarks>
public sealed class ActorAnimator
{
    /// <summary>Seconds added to an actor's clock for each network id, so a crowd that walks does not step together.</summary>
    private const double PhaseStep = 0.37;

    private readonly Dictionary<ulong, Track> _tracks = [];

    /// <summary>
    /// The pose of the actor <paramref name="id"/>, whose server state is <paramref name="anim"/>,
    /// at <paramref name="now"/> in seconds.
    /// </summary>
    // F5: sampling is what notices a new act, so it records what it saw.
    public Pose Sample(ulong id, Anim anim, double now)
    {
        var track = _tracks[id] = Seen(id, anim, now);
        if (track.Act is { } act)
        {
            var age = now - track.Since;

            // An Anim keeps only states that are acts, and every act has a length.
            if (age < Poses.ActSeconds(act)!.Value || act == ActorState.Death)
            {
                return Poses.Of(act, age);
            }
        }

        return Poses.Of(anim.Base, now + (id * PhaseStep));
    }

    /// <summary>
    /// Keeps what is known about the actors in <paramref name="ids"/> and drops the rest, so an
    /// actor that left view is treated as new when it returns.
    /// </summary>
    public void Retain(IEnumerable<ulong> ids)
    {
        var keep = ids.ToHashSet();
        foreach (var id in _tracks.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _tracks.Remove(id);
        }
    }

    private Track Seen(ulong id, Anim anim, double now)
    {
        if (!_tracks.TryGetValue(id, out var known))
        {
            return anim.Act == ActorState.Death
                ? new Track(anim.Seq, anim.Act, now - Poses.ActSeconds(ActorState.Death)!.Value)
                : new Track(anim.Seq, null, now);
        }

        return known.Seq == anim.Seq ? known : new Track(anim.Seq, anim.Act, now);
    }

    /// <summary>The act an actor is playing and the count of acts it was last seen at.</summary>
    private sealed record Track(int Seq, ActorState? Act, double Since);
}
