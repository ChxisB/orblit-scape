using Orblit.Save;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class AccountsTests : IAsyncDisposable
{
    private const string Password = "correct horse";

    private readonly List<TestServer> _servers = [];
    private readonly MemorySaveStore _store = new();
    private readonly FastHasher _hasher = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var test in _servers)
        {
            await test.DisposeAsync();
        }
    }

    private static SaveRecord Record(SaveReason reason) => new([0], reason, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static Task<TestClient> EnterAsync(TestServer test, string name, string password = Password, bool create = false) =>
        test.JoinAsync(name, password, create);

    private TestServer Start()
    {
        var test = new TestServer(options => options with { Storage = Stores.On(_store, hasher: _hasher) });
        _servers.Add(test);
        return test;
    }

    /// <summary>Makes an account for each of <paramref name="names"/> on a server, then stops it.</summary>
    private async Task MakeAsync(params string[] names)
    {
        var test = Start();
        foreach (var name in names)
        {
            await EnterAsync(test, name, create: true);
        }

        await test.DisposeAsync();
    }

    [Fact]
    public async Task ANewAccountJoinsAndTheSamePasswordGetsBackIn()
    {
        var test = Start();
        var first = await EnterAsync(test, "Ana", create: true);
        Assert.True(first.Admitted);
        await first.Link.CloseAsync();
        await test.TickAsync(12);

        var again = await EnterAsync(test, "Ana");

        Assert.True(again.Admitted);
    }

    [Fact]
    public async Task AnAccountOutlivesTheServer()
    {
        await MakeAsync("Ana");
        var test = Start();

        var ana = await EnterAsync(test, "Ana");
        var other = await EnterAsync(test, "Bo");

        Assert.True(ana.Admitted);
        Assert.Equal([LoginRefusal.BadLogin.ToWire()], other.Goodbyes);
    }

    [Fact]
    public async Task AWrongPasswordIsABadLogin()
    {
        await MakeAsync("Ana");
        var test = Start();

        var ana = await EnterAsync(test, "Ana", "wrong horse");

        Assert.False(ana.Admitted);
        Assert.Equal([LoginRefusal.BadLogin.ToWire()], ana.Goodbyes);
    }

    [Fact]
    public async Task ANameWithNoAccountIsABadLoginNotAFreeName()
    {
        var test = Start();

        var ana = await EnterAsync(test, "Ana");

        Assert.Equal([LoginRefusal.BadLogin.ToWire()], ana.Goodbyes);
        Assert.Equal(0, _hasher.Calls);
    }

    [Fact]
    public async Task AShortPasswordCannotMakeAnAccount()
    {
        var test = Start();

        var ana = await EnterAsync(test, "Ana", "short", create: true);

        Assert.Equal([LoginRefusal.WeakPassword.ToWire()], ana.Goodbyes);
        Assert.False(await _store.ExistsAsync(StoreKeys.AccountOf("ana")));
    }

    [Fact]
    public async Task ANameThatHasAnAccountCannotBeMadeAgain()
    {
        await MakeAsync("Ana");
        var test = Start();

        var again = await EnterAsync(test, "Ana", "other horse", create: true);

        Assert.Equal([LoginRefusal.AccountExists.ToWire()], again.Goodbyes);
        Assert.True((await EnterAsync(test, "Ana")).Admitted);
    }

    [Fact]
    public async Task ACharacterWithASaveAndNoAccountCannotBeClaimed()
    {
        await _store.WriteAsync(StoreKeys.SaveOf("ana"), Record(new Autosave()));
        var test = Start();

        var ana = await EnterAsync(test, "Ana", create: true);

        Assert.Equal([LoginRefusal.AccountExists.ToWire()], ana.Goodbyes);
        Assert.False(await _store.ExistsAsync(StoreKeys.AccountOf("ana")));
    }

    [Fact]
    public async Task LoginsPastTheLimitHearBusyAndCostNoHash()
    {
        string[] names = ["Ana", "Bo", "Cy", "Di", "Ed"];
        await MakeAsync(names);
        var test = Start();
        var gate = _hasher.Gate = Stores.Gate();
        var before = _hasher.Calls;
        var clients = new List<TestClient>();
        try
        {
            foreach (var name in names)
            {
                var client = test.Connect();
                await client.LoginAsync(name, Password);
                clients.Add(client);
            }
        }
        finally
        {
            gate.SetResult();
        }

        await test.UntilAsync(() => clients.All(client => client.Admitted || client.Goodbyes.Count > 0));

        Assert.Single(clients, client => client.Goodbyes.Contains(LoginRefusal.Busy.ToWire()));
        Assert.Equal(4, _hasher.Calls - before);
    }

    [Fact]
    public async Task TheSaltMakesTwoAccountsWithOnePasswordDiffer()
    {
        await MakeAsync("Ana", "Bo");

        var ana = (await _store.ReadAsync(StoreKeys.AccountOf("ana")))!.Bytes.ToArray();
        var bo = (await _store.ReadAsync(StoreKeys.AccountOf("bo")))!.Bytes.ToArray();

        Assert.NotEqual(ana, bo);
    }

    [Fact]
    public async Task ADamagedAccountRecordRefusesTheLogin()
    {
        await _store.WriteAsync(StoreKeys.AccountOf("ana"), Record(new Custom("account")));
        var test = Start();

        var ana = await EnterAsync(test, "Ana");

        Assert.Equal([LoginRefusal.Unavailable.ToWire()], ana.Goodbyes);
    }
}
