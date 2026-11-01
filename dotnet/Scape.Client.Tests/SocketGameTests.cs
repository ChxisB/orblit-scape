using System.Diagnostics;
using Scape.Core;

namespace Scape.Client.Tests;

/// <summary>
/// These run on the real clock and a real socket, so a wait polls until the thing is true and
/// gives up after <see cref="Limit"/>. A fixed sleep would pass or fail with the load on the machine.
/// </summary>
public sealed class SocketGameTests : IAsyncDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(40);

    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private async Task<GameSession> JoinAsync(string name)
    {
        var session = await _game.ConnectSocketAsync();
        session.Login(name);
        await UntilAsync($"{name} to be placed", () => Looked(session).AvatarId is not null);
        return session;
    }

    // Each look is a tick apart, so the server moves on while the test waits.
    private async Task UntilAsync(string what, Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed < Limit, $"Timed out waiting for {what}.");
            _game.Server.Loop.RunTick();
            await Task.Delay(Pace, TestContext.Current.CancellationToken);
        }
    }

    // F5: a session takes in what has arrived only when it advances, so a look advances first.
    private static GameSession Looked(GameSession session)
    {
        session.Advance();
        return session;
    }

    private static IReadOnlyList<ActorSprite> SeenBy(GameSession session) => Looked(session).Actors;

    [Fact]
    public async Task TwoClientsOnSocketsSeeEachOtherWalk()
    {
        var ana = await JoinAsync("Ana");
        var bo = await JoinAsync("Bo");
        await UntilAsync("both to see the other", () => SeenBy(ana).Count == 2 && SeenBy(bo).Count == 2);

        ana.WalkTo(new TileCoord(36, 32, 0));
        await UntilAsync(
            "Bo to see Ana at 36, 32",
            () => SeenBy(bo).Any(actor => !actor.Mine && (actor.X, actor.Y) == (36.0, 32.0)));

        var theirs = Assert.Single(SeenBy(bo), actor => !actor.Mine);
        Assert.Equal(InProcessGame.AvatarOf(ana), theirs.NetworkId);
        Assert.Equal((36.0, 32.0), (theirs.X, theirs.Y));
        Assert.Equal(InProcessGame.AvatarOf(bo), Assert.Single(SeenBy(ana), actor => !actor.Mine).NetworkId);
    }

    [Fact]
    public async Task APlayerWhoClosesTheWindowLeavesTheOthersView()
    {
        var ana = await JoinAsync("Ana");
        var bo = await JoinAsync("Bo");
        await UntilAsync("Bo to see Ana", () => SeenBy(bo).Count == 2);

        await _game.DropAsync(ana);

        await UntilAsync("Ana to leave Bo's view", () => SeenBy(bo).Count == 1);
    }
}
