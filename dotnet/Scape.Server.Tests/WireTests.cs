using Orblit.Net;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class WireTests : IAsyncDisposable
{
    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    [Fact]
    public async Task ALoginIsAnsweredWithTheNameThePlayerIsKnownBy()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");

        Assert.Equal("ana", Assert.IsType<LoginOk>(ana.Messages[0]).Name);
        Assert.Equal(SessionState.Live, Server.Host.SessionStateOf("ana"));
    }

    [Fact]
    public async Task AWalkAfterLoginIsRecordedAsThePlayersIntent()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        var target = new TileCoord(100, 200, 1);

        await ana.SendAsync(new Walk(target));

        var intent = Assert.Single(Server.Inbox.Drain());
        Assert.Equal("ana", intent.Player);
        Assert.Equal(target, Assert.IsType<Walk>(intent.Intent).Target);
    }

    [Fact]
    public async Task ChatAndInteractionsAreRecordedToo()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");

        await ana.SendAsync(new Chat("hello"));
        await ana.SendAsync(new Interact(kind: 1, id: 42, option: 2));

        Assert.Equal(2, Server.Inbox.Pending("ana"));
    }

    [Fact]
    public async Task ASecondLoginOfTheSameNameIsRefused()
    {
        var first = _test.Connect();
        await first.LoginAsync("Ana");
        var second = _test.Connect();
        await second.LoginAsync("Ana");

        Assert.Equal([LoginRefusal.NameTaken.ToWire()], second.Goodbyes);
        Assert.Empty(second.Messages);
        Assert.Empty(first.Goodbyes);
        Assert.Equal(1, Server.Host.ClientCount);
    }

    [Fact]
    public async Task NamesThatDifferOnlyInCaseAreTheSameName()
    {
        await _test.Connect().LoginAsync("Ana");
        var second = _test.Connect();
        await second.LoginAsync("ANA");
        Assert.Equal([LoginRefusal.NameTaken.ToWire()], second.Goodbyes);
    }

    [Fact]
    public async Task ADifferentNameLogsInBesideTheFirst()
    {
        await _test.Connect().LoginAsync("Ana");
        var bo = _test.Connect();
        await bo.LoginAsync("Bo");
        Assert.IsType<LoginOk>(bo.Messages[0]);
        Assert.Equal(2, Server.Host.ClientCount);
    }

    [Fact]
    public async Task AJoinThatIsNotALoginIsRefusedAsUnreadable()
    {
        var client = _test.Connect();
        client.Client.Join(MessageCodec.Encode(new Chat("hi")));
        await client.Link.IdleAsync();
        var garbage = _test.Connect();
        garbage.Client.Join([1, 2, 3]);
        await garbage.Link.IdleAsync();

        Assert.Equal([LoginRefusal.Unreadable.ToWire()], client.Goodbyes);
        Assert.Equal([LoginRefusal.Unreadable.ToWire()], garbage.Goodbyes);
        Assert.Equal(0, Server.Host.ClientCount);
    }

    [Fact]
    public async Task ALoginAfterTheLoginIsCountedAndDoesNothing()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        await ana.SendAsync(new Login("Bo"));

        Assert.Equal(1, Server.Wire.Unexpected);
        Assert.Equal(0, Server.Inbox.Pending("ana"));
        Assert.Equal(1, Server.Host.ClientCount);
    }

    [Fact]
    public async Task AMessageOverItsOwnTypesCapIsRefusedAndCounted()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        // A walk is six bytes. This one is forty.
        var walk = new byte[40];
        walk[0] = 2;

        await ana.SendBytesAsync(walk);

        Assert.Equal(1, Server.Wire.Refused[BadMessage.TooLarge]);
        Assert.Equal(0, Server.Inbox.Pending("ana"));
    }

    [Fact]
    public async Task AMessagePastTheHostBudgetNeverReachesTheDecoder()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        // One byte more than the longest message a client may send.
        var chat = new byte[MessageCodec.ClientMessageLimit + 1];
        chat[0] = 4;
        chat[1] = Wire.MaxTextBytes + 1;

        await ana.SendBytesAsync(chat);

        Assert.Equal(1, Server.Host.Rejections[InputRejection.OverBudget]);
        Assert.Empty(Server.Wire.Refused);
    }

    [Fact]
    public async Task RandomBytesFromAClientAreCountedAndNothingThrows()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        var random = new Random(3);
        for (var i = 0; i < 15; i++)
        {
            var bytes = new byte[1 + random.Next(40)];
            random.NextBytes(bytes);
            await ana.SendBytesAsync(bytes);
            Server.Loop.RunTick();
        }

        var counted = Server.Wire.Refused.Values.Sum();
        Assert.True(counted + Server.Inbox.Pending("ana") > 0);
        Assert.Equal(1, Server.Host.ClientCount);
    }

    [Fact]
    public async Task ASessionThatEndsTakesThePlayersWaitingIntentsWithIt()
    {
        var ana = _test.Connect();
        await ana.LoginAsync("Ana");
        await ana.SendAsync(new Walk(new TileCoord(1, 1, 0)));
        Assert.Equal(1, Server.Inbox.Pending("ana"));

        Server.Host.RemoveClient("ana");

        Assert.Equal(0, Server.Inbox.Pending("ana"));
    }
}
