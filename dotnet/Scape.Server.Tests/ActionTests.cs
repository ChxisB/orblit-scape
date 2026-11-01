namespace Scape.Server.Tests;

public class ActionTests
{
    private static readonly ActionKind Chat = new("chat");
    private static readonly ActionKind Skill = new("skill", delay: 3);
    private static readonly ActionKind Walk = new("walk", cancels: new CancelsAll());
    private static readonly ActionKind Eat = new("eat", cancels: new CancelsOnly(new HashSet<ActionKind> { Skill }));
    private static readonly ActionKind Guarded = new("guarded", cancels: new CancelsAll(new HashSet<ActionKind> { Chat }));

    private readonly ActionQueue _queue = new();
    private readonly List<string> _ran = [];

    private void Submit(ulong actor, ActionKind kind, string? label = null) =>
        _queue.Submit(actor, kind, () => _ran.Add(label ?? kind.Name));

    private void Tick(int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            _queue.Advance();
            _queue.RunDue();
        }
    }

    // ActionKind.

    [Fact]
    public void RefusesAnEmptyNameAndANegativeDelay()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ActionKind(""));
        Assert.ThrowsAny<ArgumentException>(() => new ActionKind("x", delay: -1));
    }

    [Fact]
    public void AWalkCancelsEverythingAChatCancelsNothing()
    {
        Assert.True(Walk.Interrupts(Skill));
        Assert.True(Walk.Interrupts(Walk));
        Assert.False(Chat.Interrupts(Skill));
    }

    [Fact]
    public void CancelsAllButTheExceptionsOrOnlyTheListedKinds()
    {
        Assert.False(Guarded.Interrupts(Chat));
        Assert.True(Guarded.Interrupts(Skill));
        Assert.True(Eat.Interrupts(Skill));
        Assert.False(Eat.Interrupts(Walk));
    }

    // ActionQueue.

    [Fact]
    public void RunsAnActionWithNoDelayInTheTickItWasQueued()
    {
        _queue.Advance();
        Submit(1, Chat);
        _queue.RunDue();
        Assert.Equal(["chat"], _ran);
    }

    [Fact]
    public void HoldsAnActionUntilItsDelayHasPassed()
    {
        _queue.Advance();
        Submit(1, Skill);
        _queue.RunDue();
        Tick(2);
        Assert.Empty(_ran);
        Tick();
        Assert.Equal(["skill"], _ran);
    }

    [Fact]
    public void RunsAnActionOnce()
    {
        _queue.Advance();
        Submit(1, Chat);
        _queue.RunDue();
        Tick(3);
        Assert.Equal(["chat"], _ran);
    }

    [Fact]
    public void AnActionCancelledBeforeItsDelayEndsNeverRuns()
    {
        _queue.Advance();
        Submit(1, Skill);
        Tick();
        Submit(1, Walk);
        Tick(10);
        Assert.Equal(["walk"], _ran);
    }

    [Fact]
    public void AnActionThatCancelsNothingLeavesARunningOneAlone()
    {
        _queue.Advance();
        Submit(1, Skill);
        Tick();
        Submit(1, Chat);
        Tick(5);
        Assert.Equal(["chat", "skill"], _ran);
    }

    [Fact]
    public void ACancelOnlyReachesTheSameActor()
    {
        _queue.Advance();
        Submit(1, Skill, "one");
        Submit(2, Skill, "two");
        Tick();
        Submit(1, Walk);
        Tick(5);
        Assert.Equal(["walk", "two"], _ran);
    }

    [Fact]
    public void ACancelByAListedKindSparesTheOthers()
    {
        _queue.Advance();
        Submit(1, Skill);
        Submit(1, Walk, "later walk");
        Tick();
        Submit(1, Eat);
        Tick(5);
        Assert.Contains("later walk", _ran);
        Assert.DoesNotContain("skill", _ran);
    }

    [Fact]
    public void TwoActionsQueuedInOneTickRunInTheOrderTheyCame()
    {
        _queue.Advance();
        Submit(1, Chat, "first");
        Submit(2, Chat, "second");
        Submit(1, Chat, "third");
        _queue.RunDue();
        Assert.Equal(["first", "second", "third"], _ran);
    }

    [Fact]
    public void AnEarlierActionInAPassCanCancelALaterOne()
    {
        _queue.Advance();
        _queue.Submit(1, Chat, () =>
        {
            _ran.Add("chat");
            Submit(1, Walk);
        });
        _queue.Submit(1, Skill, () => _ran.Add("skill"));
        _queue.RunDue();
        Tick(5);
        Assert.Equal(["chat", "walk"], _ran);
    }

    [Fact]
    public void AnActionQueuedWhileOthersRunWaitsForTheNextTick()
    {
        _queue.Advance();
        _queue.Submit(1, Chat, () =>
        {
            _ran.Add("outer");
            Submit(2, Chat, "inner");
        });
        _queue.RunDue();
        Assert.Equal(["outer"], _ran);
        Tick();
        Assert.Equal(["outer", "inner"], _ran);
    }

    [Fact]
    public void CountsWhatIsWaitingForOneActor()
    {
        _queue.Advance();
        Submit(1, Skill);
        Submit(1, Skill);
        Submit(2, Skill);
        Assert.Equal(2, _queue.Pending(1));
        Assert.Equal(0, _queue.Pending(3));
        Submit(1, Walk);
        Assert.Equal(1, _queue.Pending(1));
    }
}
