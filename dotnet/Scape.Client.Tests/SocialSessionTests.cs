using Scape.Core;

namespace Scape.Client.Tests;

public sealed class SocialSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task ChatShowsOverTheSpeakerForThoseNearby()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");

        ana.Say("hello");
        await _game.SettleAsync();

        Assert.Equal([new Bubble(InProcessGame.AvatarOf(ana), "hello")], bo.Bubbles);
        Assert.Equal(InProcessGame.AvatarOf(ana), Assert.Single(ana.Bubbles).NetworkId);
    }

    [Fact]
    public async Task ChatGoesAfterFiveSecondsAndANewerLineReplacesAnOlder()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        ana.Say("first");
        await _game.SettleAsync();

        _game.Seconds += GameSession.BubbleSeconds - 1;
        Assert.Equal("first", Assert.Single(bo.Bubbles).Text);

        ana.Say("second");
        await _game.SettleAsync();
        Assert.Equal("second", Assert.Single(bo.Bubbles).Text);

        _game.Seconds += GameSession.BubbleSeconds + 1;
        Assert.Empty(bo.Bubbles);
        bo.Advance();
        Assert.Empty(bo.Bubbles);
    }

    [Fact]
    public async Task AMutedSpeakerIsToldAndNobodySeesABubble()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        _game.Server.Social.Mute("ana");

        ana.Say("hello");
        await _game.SettleAsync();

        Assert.Equal(["You are muted."], ana.Notices);
        Assert.Empty(bo.Bubbles);
    }

    [Fact]
    public async Task WhispersArriveWithTheSenderAndAreKeptInOrder()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");

        ana.Whisper("bo", "one");
        ana.Whisper("Bo", "two");
        await _game.SettleAsync();

        Assert.Equal([new Heard("ana", "one"), new Heard("ana", "two")], bo.Whispers);
        Assert.Empty(ana.Whispers);
    }

    [Fact]
    public async Task AWhisperToSomeoneOfflineComesBackAsANotice()
    {
        var ana = await _game.JoinAsync("Ana");

        ana.Whisper("Nobody", "hi");
        await _game.SettleAsync();

        Assert.Equal(["That player is not online."], ana.Notices);
    }

    [Fact]
    public async Task WhispersKeepTheNewestHundred()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");

        for (var i = 0; i < 110; i++)
        {
            ana.Whisper("bo", $"m{i}");
            if (i % 10 == 9)
            {
                await _game.SettleAsync();
            }
        }

        await _game.SettleAsync(2);

        Assert.Equal(100, bo.Whispers.Count);
        Assert.Equal("m109", bo.Whispers[^1].Text);
        Assert.Equal("m10", bo.Whispers[0].Text);
    }

    [Fact]
    public async Task ListsFollowWhatTheServerSendsWithFriendsOnline()
    {
        var ana = await _game.JoinAsync("Ana");
        ana.EditList(ListKind.Friends, "Bo", add: true);
        ana.EditList(ListKind.Ignored, "Cy", add: true);
        await _game.SettleAsync();
        Assert.Equal([new Friend("bo", false)], ana.Friends);
        Assert.Equal(["cy"], ana.Ignored);

        await _game.JoinAsync("Bo");
        await _game.SettleAsync();
        Assert.Equal([new Friend("bo", true)], ana.Friends);

        ana.EditList(ListKind.Ignored, "cy", add: false);
        await _game.SettleAsync();
        Assert.Empty(ana.Ignored);
    }

    [Fact]
    public async Task AnIgnoredPlayerIsNotHeard()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        bo.EditList(ListKind.Ignored, "Ana", add: true);
        await _game.SettleAsync();

        ana.Say("hello");
        await _game.SettleAsync();

        Assert.Empty(bo.Bubbles);
    }

    [Fact]
    public async Task TextItShouldNotSendIsRefusedBeforeItIsSent()
    {
        var ana = await _game.JoinAsync("Ana");

        Assert.ThrowsAny<ArgumentException>(() => ana.Say(""));
        Assert.ThrowsAny<ArgumentException>(() => ana.Whisper("not valid!", "x"));
        Assert.ThrowsAny<ArgumentException>(() => ana.EditList(ListKind.Friends, "", add: true));
    }
}
