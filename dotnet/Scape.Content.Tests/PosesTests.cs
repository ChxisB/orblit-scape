using Scape.Core;

namespace Scape.Content.Tests;

public class PosesTests
{
    private static double LengthOf(ActorState act) => Poses.ActSeconds(act)!.Value;

    [Fact]
    public void OnlyAnActHasALength()
    {
        foreach (var state in Enum.GetValues<ActorState>())
        {
            Assert.Equal(state.IsAct, Poses.ActSeconds(state) is not null);
        }
    }

    [Fact]
    public void WalkingSwingsTheLegsAgainstEachOther()
    {
        var pose = Poses.Of(ActorState.Walk, 0.1);

        Assert.NotEqual(0, pose.LegL);
        Assert.Equal(-pose.LegL, pose.LegR);
        Assert.NotEqual(Math.Sign(pose.LegL) * Math.Abs(pose.ArmL), pose.ArmL);
    }

    [Fact]
    public void RunningReachesFurtherThanWalking()
    {
        static double Most(ActorState state) => Enumerable.Range(0, 100).Max(i => Math.Abs(Poses.Of(state, i / 50.0).LegL));

        Assert.True(Most(ActorState.Run) > Most(ActorState.Walk));
    }

    [Fact]
    public void StandingStillIsNearlyStill()
    {
        var pose = Poses.Of(ActorState.Idle, 3);

        Assert.Equal(0, pose.LegL);
        Assert.True(Math.Abs(pose.ArmL) < 0.05);
    }

    [Fact]
    public void ASwingLiftsTheArmMidWayAndPutsItBackAtTheEnd()
    {
        var length = LengthOf(ActorState.Attack);

        Assert.Equal(0, Poses.Of(ActorState.Attack, 0).ArmR, 1e-9);
        Assert.True(Poses.Of(ActorState.Attack, length / 2).ArmR < -2);
        Assert.Equal(0, Poses.Of(ActorState.Attack, length).ArmR, 1e-9);
    }

    [Fact]
    public void MakingBobsBothHandsAndRestsAtTheEnds()
    {
        var length = LengthOf(ActorState.Make);

        Assert.Equal(0, Poses.Of(ActorState.Make, 0).ArmR, 1e-9);
        Assert.True(Poses.Of(ActorState.Make, length / 4).ArmR < -0.8);
        Assert.Equal(0, Poses.Of(ActorState.Make, length).ArmR, 1e-9);
    }

    [Fact]
    public void AHurtActorLeansBack()
    {
        var length = LengthOf(ActorState.Hurt);

        Assert.True(Poses.Of(ActorState.Hurt, length / 2).Lean < 0);
    }

    [Fact]
    public void DeathFallsAndThenHolds()
    {
        var length = LengthOf(ActorState.Death);

        Assert.Equal(0, Poses.Of(ActorState.Death, 0).Lie);
        Assert.True(Poses.Of(ActorState.Death, length / 2).Lie is > 0 and < 1);
        Assert.Equal(1, Poses.Of(ActorState.Death, length).Lie);
        Assert.Equal(1, Poses.Of(ActorState.Death, length * 5).Lie);
    }

    [Fact]
    public void AnActHeldPastItsLengthStaysAtItsLastPose()
    {
        var length = LengthOf(ActorState.Gather);

        Assert.Equal(Poses.Of(ActorState.Gather, length).ArmR, Poses.Of(ActorState.Gather, length * 4).ArmR, 1e-9);
    }
}
