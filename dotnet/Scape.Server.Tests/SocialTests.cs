using Orblit.Core;
using Orblit.Net;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class SocialTests : IAsyncDisposable
{
    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static EditList Friend(string name, bool add = true) => new(ListKind.Friends, add, name);

    private static EditList Ignore(string name) => new(ListKind.Ignored, add: true, name);

    // Public chat.

    [Fact]
    public async Task ChatReachesTheSpeakerAndAPlayerNearbyNamingTheSpeaker()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");

        await ana.SendAsync(new Chat("hello"));
        await _test.TickAsync();

        var heard = Assert.Single(bo.Heard);
        Assert.Equal(ana.Avatar, heard.Speaker);
        Assert.Equal("hello", heard.Text);
        Assert.Equal(ana.Avatar, Assert.Single(ana.Heard).Speaker);
    }

    [Fact]
    public async Task ChatStopsAtTheChatRadiusThoughTheSpeakerIsStillInView()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await bo.SendAsync(new Walk(At(52, 32)));
        await _test.TickAsync(41);
        Assert.Equal(2, ana.Client.EntityCount);

        await ana.SendAsync(new Chat("can you hear me"));
        await _test.TickAsync();

        Assert.Empty(bo.Heard);
        Assert.Single(ana.Heard);
    }

    [Fact]
    public async Task ChatIsNotHeardByAPlayerWhoIgnoresTheSpeaker()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        var cy = await _test.JoinAsync("Cy");
        await bo.SendAsync(Ignore("ana"));

        await ana.SendAsync(new Chat("hello"));
        await _test.TickAsync();

        Assert.Empty(bo.Heard);
        Assert.Single(cy.Heard);
        Assert.Single(ana.Heard);
    }

    [Fact]
    public async Task AMutedPlayerIsToldSoAndNobodyHearsThem()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        Server.Social.Mute("ANA");
        Assert.True(Server.Social.IsMuted("ana"));

        await ana.SendAsync(new Chat("hello"));
        await _test.TickAsync();

        Assert.Equal(["You are muted."], ana.Notices);
        Assert.Empty(bo.Heard);
        Assert.Empty(ana.Heard);

        Server.Social.Unmute("ana");
        await ana.SendAsync(new Chat("hello again"));
        await _test.TickAsync();

        Assert.Single(bo.Heard);
    }

    // Whispers.

    [Fact]
    public async Task AWhisperGoesToTheNamedPlayerOnlyByAnyCaseOfTheirName()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        var cy = await _test.JoinAsync("Cy");

        await ana.SendAsync(new Whisper(to: "BO", text: "psst"));
        await _test.TickAsync();

        var whisper = Assert.Single(bo.Whispers);
        Assert.Equal("ana", whisper.From);
        Assert.Equal("psst", whisper.Text);
        Assert.Empty(cy.Whispers);
        Assert.Empty(ana.Whispers);
    }

    [Fact]
    public async Task AWhisperReachesAPlayerFarAwayOnTheMap()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await bo.SendAsync(new Walk(At(110, 32)));
        await _test.TickAsync(80);

        await ana.SendAsync(new Whisper(to: "Bo", text: "still there"));
        await _test.TickAsync();

        Assert.Single(bo.Whispers);
    }

    [Fact]
    public async Task AWhisperToANameThatIsOfflineIsAnsweredWithANotice()
    {
        var ana = await _test.JoinAsync("Ana");

        await ana.SendAsync(new Whisper(to: "Nobody", text: "hi"));
        await _test.TickAsync();

        Assert.Equal(["That player is not online."], ana.Notices);
    }

    [Fact]
    public async Task AWhisperToAPlayerWhoIgnoresTheSenderLooksTheSameAsOffline()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await bo.SendAsync(Ignore("Ana"));

        await ana.SendAsync(new Whisper(to: "Bo", text: "hi"));
        await _test.TickAsync();

        Assert.Empty(bo.Whispers);
        Assert.Equal(["That player is not online."], ana.Notices);
    }

    [Fact]
    public async Task WhispersFromAMutedPlayerAreRefused()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        Server.Social.Mute("ana");

        await ana.SendAsync(new Whisper(to: "Bo", text: "hi"));
        await _test.TickAsync();

        Assert.Empty(bo.Whispers);
        Assert.Equal(["You are muted."], ana.Notices);
    }

    // Lists.

    [Fact]
    public async Task ALoginIsSentEmptyLists()
    {
        var ana = await _test.JoinAsync("Ana");

        Assert.Empty(ana.Lists.Friends);
        Assert.Empty(ana.Lists.Ignored);
    }

    [Fact]
    public async Task ListsOfAndRestoreCarryTheListsAcrossASave()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(Friend("Bo"));
        await ana.SendAsync(Ignore("Cy"));
        await _test.TickAsync();

        var saved = Server.Social.ListsOf("ana");
        Server.Social.Restore("ana", friends: ["di"], ignored: []);
        await _test.TickAsync();

        Assert.Equal(["bo"], saved.Friends);
        Assert.Equal(["cy"], saved.Ignored);
        Assert.Equal([new Friend("di", Online: false)], ana.Lists.Friends);
        Assert.Empty(ana.Lists.Ignored);
        Assert.Empty(Server.Social.ListsOf("nobody").Friends);
    }

    [Fact]
    public async Task AddingAndRemovingANameSendsTheListBack()
    {
        var ana = await _test.JoinAsync("Ana");

        await ana.SendAsync(Friend("Bo"));
        await ana.SendAsync(Ignore("Cy"));
        await _test.TickAsync();
        Assert.Equal([new Friend("bo", Online: false)], ana.Lists.Friends);
        Assert.Equal(["cy"], ana.Lists.Ignored);

        await ana.SendAsync(Friend("BO", add: false));
        await _test.TickAsync();
        Assert.Empty(ana.Lists.Friends);
    }

    [Fact]
    public async Task ANameAlreadyOnAListChangesNothingAndSendsNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(Friend("Bo"));
        await _test.TickAsync();
        var before = ana.Messages.OfType<SocialLists>().Count();

        await ana.SendAsync(Friend("bo"));
        await ana.SendAsync(Friend("Cy", add: false));
        await _test.TickAsync();

        Assert.Equal(before, ana.Messages.OfType<SocialLists>().Count());
    }

    [Fact]
    public async Task AListStopsAtFiftyNames()
    {
        var ana = await _test.JoinAsync("Ana");
        for (var i = 0; i <= Wire.MaxListEntries; i++)
        {
            await ana.SendAsync(Friend($"n{i}"));
            if (i % 10 == 9)
            {
                await _test.TickAsync();
            }
        }

        await _test.TickAsync(2);

        Assert.Equal(Wire.MaxListEntries, ana.Lists.Friends.Count);
        Assert.Equal(["That list is full."], ana.Notices);
    }

    [Fact]
    public async Task APlayerCannotAddThemselves()
    {
        var ana = await _test.JoinAsync("Ana");

        await ana.SendAsync(Ignore("ANA"));
        await _test.TickAsync();

        Assert.Empty(ana.Lists.Ignored);
        Assert.Equal(["You cannot add yourself."], ana.Notices);
    }

    [Fact]
    public async Task ListsShowAFriendComeOnlineAndGo()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(Friend("Bo"));
        await _test.TickAsync();

        var bo = await _test.JoinAsync("Bo");
        Assert.Equal([new Friend("bo", Online: true)], ana.Lists.Friends);

        await _test.LeaveAsync(bo);
        await _test.TickAsync(15);
        Assert.Equal([new Friend("bo", Online: false)], ana.Lists.Friends);
    }

    [Fact]
    public async Task APlayerIsSentTheListsTheyLeftWithWhenTheyReturn()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(Friend("Bo"));
        await _test.LeaveAsync(ana);
        await _test.TickAsync(15);

        var again = await _test.JoinAsync("Ana");

        Assert.Equal([new Friend("bo", Online: false)], again.Lists.Friends);
    }

    // Planes.

    [Fact]
    public void ChatDoesNotCrossPlanesAtTheSameTile()
    {
        var standing = new Dictionary<string, (ulong NetworkId, TileCoord Tile)>
        {
            ["ana"] = (1, new TileCoord(5, 5, 0)),
            ["bo"] = (2, new TileCoord(5, 5, 1)),
            ["cy"] = (3, new TileCoord(5, 5, 0)),
        };
        var hosting = new Recorder();
        var registry = new Registry();
        using var world = new World();
        var active = new ActiveCells(new Grid(16), 1, TimeSpan.Zero, () => TimeSpan.Zero);
        Social.Register(
            registry,
            new Services(world, OpenWorld.Map(), hosting, active),
            player => standing.TryGetValue(player, out var found) ? found : null);
        foreach (var name in standing.Keys)
        {
            registry.PlayerJoined(name);
        }

        registry.Deliver("ana", new Chat("hello"));

        Assert.Empty(hosting.Told["bo"].OfType<Said>());
        Assert.Single(hosting.Told["cy"].OfType<Said>());
    }

    private sealed class Recorder : IHosting
    {
        public Dictionary<string, List<ServerMessage>> Told { get; } = [];

        public ulong Spawn(ulong entity, string? owner = null) => 0;

        public void Despawn(ulong entity)
        {
        }

        public void Tell(string player, ServerMessage message)
        {
            if (!Told.TryGetValue(player, out var told))
            {
                Told[player] = told = [];
            }

            told.Add(message);
        }
    }
}

/// <summary>What a client was told, read the way the social tests ask.</summary>
file static class SocialReading
{
    extension(TestClient client)
    {
        public IReadOnlyList<Said> Heard => [.. client.Messages.OfType<Said>()];

        public IReadOnlyList<Whispered> Whispers => [.. client.Messages.OfType<Whispered>()];

        public SocialLists Lists => client.Messages.OfType<SocialLists>().Last();
    }
}
