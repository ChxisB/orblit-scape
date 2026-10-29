using Scape.Core;

namespace Scape.Content;

/// <summary>
/// How a rig is turned at one moment, in radians. The arms and legs swing
/// forward when positive, <see cref="Lean"/> bends the body forward, and
/// <see cref="Lie"/> is how far the actor has fallen onto its back, from 0
/// upright to 1 flat.
/// </summary>
public sealed record Pose
{
    public double LegL { get; init; }

    public double LegR { get; init; }

    public double ArmL { get; init; }

    public double ArmR { get; init; }

    public double Lean { get; init; }

    public double Lie { get; init; }
}

/// <summary>How an actor's rig moves in each state.</summary>
public static class Poses
{
    /// <summary>Seconds an act takes. A resting state has no length.</summary>
    public static double? ActSeconds(ActorState state) => state switch
    {
        ActorState.Attack => 0.5,
        ActorState.Hurt => 0.4,
        ActorState.Death => 1.0,
        ActorState.Gather => 0.8,
        ActorState.Make => 0.8,
        _ => null,
    };

    /// <summary>
    /// The pose of <paramref name="state"/> after <paramref name="seconds"/>. A resting
    /// state repeats and an act plays once, then holds its last pose.
    /// </summary>
    // shortcut: poses come from sine curves, not authored clips. Swap `Of`
    // for a clip sampler when the drawing app has art.
    public static Pose Of(ActorState state, double seconds) => state switch
    {
        ActorState.Idle => Idle(seconds),
        ActorState.Walk => Stride(seconds, rate: 1.8, reach: 0.6),
        ActorState.Run => Stride(seconds, rate: 2.8, reach: 1.0, lean: 0.2),
        ActorState.Attack => Swing(Progress(state, seconds)),
        ActorState.Hurt => Recoil(Progress(state, seconds)),
        ActorState.Death => Fall(Progress(state, seconds)),
        ActorState.Gather => Work(Progress(state, seconds)),
        ActorState.Make => Craft(Progress(state, seconds)),
        _ => throw new InvalidOperationException($"{state} is not an actor state."),
    };

    // Value is there because every state that reaches here is an act.
    private static double Progress(ActorState act, double seconds) =>
        Math.Clamp(seconds / ActSeconds(act)!.Value, 0.0, 1.0);

    private static Pose Idle(double seconds)
    {
        var sway = 0.04 * Math.Sin(seconds * 2);
        return new Pose { ArmL = sway, ArmR = -sway };
    }

    private static Pose Stride(double seconds, double rate, double reach, double lean = 0)
    {
        var leg = reach * Math.Sin(seconds * rate * 2 * Math.PI);
        return new Pose { LegL = leg, LegR = -leg, ArmL = -leg * 0.8, ArmR = leg * 0.8, Lean = lean };
    }

    private static Pose Swing(double progress)
    {
        var arc = Math.Sin(progress * Math.PI);
        return new Pose { ArmR = -2.2 * arc, Lean = 0.25 * arc };
    }

    private static Pose Recoil(double progress)
    {
        var arc = Math.Sin(progress * Math.PI);
        return new Pose { ArmL = -0.6 * arc, ArmR = -0.6 * arc, Lean = -0.35 * arc };
    }

    private static Pose Fall(double progress)
    {
        var left = 1 - progress;
        return new Pose { Lie = 1 - (left * left) };
    }

    private static Pose Craft(double progress)
    {
        var beat = Math.Abs(Math.Sin(progress * Math.PI * 2));
        return new Pose { ArmL = -0.9 * beat, ArmR = -0.9 * beat, Lean = 0.2 };
    }

    private static Pose Work(double progress)
    {
        var arc = Math.Sin(progress * Math.PI);
        return new Pose { ArmL = -1.2 * arc, ArmR = -1.2 * arc, Lean = 0.35 };
    }
}
