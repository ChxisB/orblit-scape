using Scape.Core;

namespace Scape.Client.Tests;

public sealed class SessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    // Drawing runs behind the newest state by the interpolation delay, so the clock has to pass it
    // before the newest state shows.
    private void CatchUp(GameSession session)
    {
        _game.Seconds += GameSession.InterpolationDelay.TotalSeconds;
        session.Advance();
    }

    private static double DrawnX(GameSession session)
    {
        session.Advance();
        return session.Actors.Single(actor => actor.Mine).X;
    }

    [Fact]
    public async Task ALoginGivesANameAnAvatarAndAnActorThatIsMine()
    {
        var ana = await _game.JoinAsync("Ana");

        Assert.True(ana.IsLoggedIn);
        Assert.Equal("ana", ana.Name);
        Assert.NotNull(ana.AvatarId);
        CatchUp(ana);
        var actor = Assert.Single(ana.Actors);
        Assert.True(actor.Mine);
        Assert.Equal(ana.AvatarId, actor.NetworkId);
    }

    [Fact]
    public async Task ANameAlreadyInUseIsRefusedWithTheReason()
    {
        await _game.JoinAsync("Ana");
        var second = _game.Connect();
        second.Login("ANA");
        await _game.PumpAsync();

        Assert.False(second.IsLoggedIn);
        Assert.Equal(LoginRefusal.NameTaken.ToWire(), second.Refusal);
    }

    [Fact]
    public async Task AnotherPlayerIsAnActorThatIsNotMine()
    {
        var ana = await _game.JoinAsync("Ana");
        await _game.JoinAsync("Bo");
        _game.Seconds += InProcessGame.TickSeconds;
        await _game.TickAsync();
        CatchUp(ana);

        Assert.Equal(2, ana.Actors.Count);
        Assert.Single(ana.Actors, actor => actor.Mine);
    }

    [Fact]
    public async Task AWalkMovesTheActorSmoothlyAcrossEachTick()
    {
        var ana = await _game.JoinAsync("Ana");
        ana.WalkTo(new TileCoord(36, 32, 0));
        await _game.PumpAsync();

        var seen = new List<double>();
        for (var tick = 0; tick < 5; tick++)
        {
            for (var frame = 0; frame < 6; frame++)
            {
                seen.Add(DrawnX(ana));
                _game.Seconds += InProcessGame.TickSeconds / 6;
            }

            await _game.TickAsync();
        }

        for (var i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i] - seen[i - 1] <= (1.0 / 6) + 1e-3);
            Assert.True(seen[i] >= seen[i - 1] - 1e-6);
        }

        Assert.Contains(seen, x => x != Math.Round(x));
    }

    [Fact]
    public async Task AWalkEndsOnTheTileThatWasClicked()
    {
        var ana = await _game.JoinAsync("Ana");
        ana.WalkTo(new TileCoord(35, 32, 0));
        await _game.PumpAsync();

        for (var i = 0; i < 6; i++)
        {
            _game.Seconds += InProcessGame.TickSeconds;
            await _game.TickAsync();
        }

        CatchUp(ana);

        var actor = Assert.Single(ana.Actors);
        Assert.Equal(35, actor.X);
        Assert.Equal(32, actor.Y);
    }
}
