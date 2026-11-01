using Scape.Core;

namespace Scape.Server.Tests;

public class InboxTests
{
    private static Walk WalkTo(int x) => new(new TileCoord(x, 0, 0));

    [Fact]
    public void HandsIntentsBackInTheOrderTheyCameThenIsEmpty()
    {
        var inbox = new Inbox();
        inbox.Add("ana", WalkTo(1));
        inbox.Add("bo", WalkTo(2));
        inbox.Add("ana", WalkTo(3));

        var drained = inbox.Drain();

        Assert.Equal(["ana", "bo", "ana"], drained.Select(each => each.Player));
        Assert.Equal([1, 2, 3], drained.Select(each => ((Walk)each.Intent).Target.X));
        Assert.Empty(inbox.Drain());
    }

    [Fact]
    public void KeepsTheFirstFewOfAPlayerAndCountsTheRestAsDropped()
    {
        var inbox = new Inbox(perPlayer: 2);
        for (var i = 0; i < 5; i++)
        {
            inbox.Add("ana", WalkTo(i));
        }

        inbox.Add("bo", WalkTo(9));

        Assert.Equal(2, inbox.Pending("ana"));
        Assert.Equal(1, inbox.Pending("bo"));
        Assert.Equal(3, inbox.Dropped);
    }

    [Fact]
    public void DrainingMakesRoomAgain()
    {
        var inbox = new Inbox(perPlayer: 1);
        inbox.Add("ana", WalkTo(1));
        inbox.Drain();
        inbox.Add("ana", WalkTo(2));
        Assert.Equal(1, inbox.Pending("ana"));
        Assert.Equal(0, inbox.Dropped);
    }

    [Fact]
    public void ForgetsAPlayerWhoLeft()
    {
        var inbox = new Inbox();
        inbox.Add("ana", WalkTo(1));
        inbox.Add("bo", WalkTo(2));
        inbox.Forget("ana");
        Assert.Equal(0, inbox.Pending("ana"));
        Assert.Equal(["bo"], inbox.Drain().Select(each => each.Player));
    }

    [Fact]
    public void RefusesALimitBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Inbox(perPlayer: 0));
    }
}
