using System.Diagnostics;
using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public class ActorAnimatorTests
{
    private readonly ActorAnimator _animator = new();

    private static double AttackSeconds => Poses.ActSeconds(ActorState.Attack)!.Value;

    [Fact]
    public void StandingIsTheIdlePose()
    {
        var pose = _animator.Sample(1, new Anim(), 5);

        Assert.Equal(0, pose.LegL);
        Assert.Equal(0, pose.Lie);
    }

    [Fact]
    public void WalkingSwingsTheLegs()
    {
        var walking = new Anim(ActorState.Walk);

        var swings = Enumerable.Range(0, 20).Select(i => Math.Sign(_animator.Sample(1, walking, i / 10.0).LegL)).ToHashSet();

        Assert.Contains(-1, swings);
        Assert.Contains(1, swings);
    }

    [Fact]
    public void TwoWalkersAreNotInStep()
    {
        var walking = new Anim(ActorState.Walk);

        var a = _animator.Sample(1, walking, 0.3);
        var b = _animator.Sample(2, walking, 0.3);

        Assert.NotEqual(a.LegL, b.LegL);
    }

    [Fact]
    public void ANewCountPlaysTheActFromItsStart()
    {
        _animator.Sample(1, new Anim(), 0);
        var swung = new Anim().Playing(ActorState.Attack);

        var start = _animator.Sample(1, swung, 1);
        var middle = _animator.Sample(1, swung, 1.25);

        Assert.Equal(0, start.ArmR, 1e-9);
        Assert.True(middle.ArmR < -2);
    }

    [Fact]
    public void TheActorRestsAgainWhenTheActIsOver()
    {
        _animator.Sample(1, new Anim(), 0);
        var swung = new Anim().Playing(ActorState.Attack);
        _animator.Sample(1, swung, 1);

        var after = _animator.Sample(1, swung, 1 + AttackSeconds + 1);

        Assert.True(Math.Abs(after.ArmR) < 0.05);
    }

    [Fact]
    public void TheSameActAgainRestarts()
    {
        _animator.Sample(1, new Anim(), 0);
        var first = new Anim().Playing(ActorState.Attack);
        _animator.Sample(1, first, 1);
        _animator.Sample(1, first, 1.25);

        var second = first.Playing(ActorState.Attack);
        var pose = _animator.Sample(1, second, 3);

        Assert.Equal(0, pose.ArmR, 1e-9);
    }

    [Fact]
    public void TheSameCountIsNotPlayedTwice()
    {
        _animator.Sample(1, new Anim(), 0);
        var swung = new Anim().Playing(ActorState.Attack);
        _animator.Sample(1, swung, 1);

        var later = _animator.Sample(1, swung, 1.25);
        var again = _animator.Sample(1, swung, 1.25);

        Assert.Equal(later.ArmR, again.ArmR);
    }

    [Fact]
    public void ANewCountWithNoActClearsTheOldOne()
    {
        _animator.Sample(1, new Anim(act: ActorState.Death, seq: 4), 0);

        var pose = _animator.Sample(1, new Anim(), 1);

        Assert.Equal(0, pose.Lie);
    }

    [Fact]
    public void ACountThatWrappedStillPlays()
    {
        _animator.Sample(1, new Anim(), 0);
        var last = new Anim(act: ActorState.Attack, seq: Anim.MaxSeq);
        _animator.Sample(1, last, 1);

        var wrapped = last.Playing(ActorState.Hurt);
        Assert.Equal(0, wrapped.Seq);
        _animator.Sample(1, wrapped, 2);
        var pose = _animator.Sample(1, wrapped, 2.2);

        Assert.True(pose.Lean < 0);
    }

    [Fact]
    public void AnActThatIsOverDoesNotReplayOnFirstSight()
    {
        var swung = new Anim(act: ActorState.Attack, seq: 9);

        var pose = _animator.Sample(1, swung, 0.25);

        Assert.True(Math.Abs(pose.ArmR) < 0.05);
    }

    [Fact]
    public void ADeathShowsAsFallenOnFirstSight()
    {
        var dead = new Anim(act: ActorState.Death, seq: 9);

        Assert.Equal(1, _animator.Sample(1, dead, 100).Lie);
    }

    [Fact]
    public void AnActorThatLeftViewIsTreatedAsNew()
    {
        var dead = new Anim(act: ActorState.Death, seq: 9);
        _animator.Sample(1, dead, 0);

        _animator.Retain([2]);
        var swung = new Anim(act: ActorState.Attack, seq: 9);
        var pose = _animator.Sample(1, swung, 5.25);

        Assert.True(Math.Abs(pose.ArmR) < 0.05);
    }

    [Fact]
    public void AnActThatIsOverStaysOverAfterFirstSight()
    {
        var swung = new Anim(act: ActorState.Attack, seq: 9);
        _animator.Sample(1, swung, 0);

        Assert.True(Math.Abs(_animator.Sample(1, swung, 0.25).ArmR) < 0.05);
    }

    [Fact]
    public void AnActorThatLeftViewDeadIsNotStillFallenWhenItReturns()
    {
        _animator.Sample(1, new Anim(act: ActorState.Death, seq: 9), 0);

        _animator.Retain([2]);

        Assert.Equal(0, _animator.Sample(1, new Anim(seq: 9), 5).Lie);
    }

    [Fact]
    public void RetainingAnActorKeepsItsAct()
    {
        _animator.Sample(1, new Anim(), 0);
        var swung = new Anim().Playing(ActorState.Attack);
        _animator.Sample(1, swung, 1);

        _animator.Retain([1, 2]);

        Assert.True(_animator.Sample(1, swung, 1.25).ArmR < -2);
    }

    [Fact]
    public void AHundredActorsForTenSecondsOfFramesIsCheap()
    {
        var anims = Enumerable.Range(0, 100)
            .Select(i => i % 2 == 0 ? new Anim(ActorState.Walk) : new Anim().Playing(ActorState.Attack))
            .ToList();
        var watch = Stopwatch.StartNew();

        for (var frame = 0; frame < 600; frame++)
        {
            for (var id = 0; id < anims.Count; id++)
            {
                _animator.Sample((ulong)id, anims[id], frame / 60.0);
            }
        }

        // This only catches a mistake of an order of magnitude. It is not a frame budget, which
        // needs a drawing app on a device.
        Assert.True(watch.ElapsedMilliseconds < 2000);
    }
}
