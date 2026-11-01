using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class ActorsSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private long Chicken() => (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, new TileCoord(33, 32, 0))).NetworkId;

    [Fact]
    public async Task APlayerSeesTheirOwnLookAndAnotherPlayerInView()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        await _game.TickAndDrawAsync(ana, 2);
        bo.Advance();
        var boId = InProcessGame.AvatarOf(bo);

        Assert.NotNull(ana.LookOf(InProcessGame.AvatarOf(ana)));
        Assert.Equal(bo.LookOf(boId), ana.LookOf(boId));
        Assert.Equal(new Anim(), ana.AnimOf(boId));
    }

    [Fact]
    public async Task ACreatureHasWhatItIsDoingButNoLook()
    {
        var ana = await _game.JoinAsync("Ana");
        var chicken = Chicken();
        await _game.TickAndDrawAsync(ana, 2);

        Assert.NotNull(ana.AnimOf(chicken));
        Assert.Null(ana.LookOf(chicken));
    }

    [Fact]
    public async Task SomethingOutOfViewHasNeither()
    {
        var ana = await _game.JoinAsync("Ana");

        Assert.Null(ana.LookOf(9999));
        Assert.Null(ana.AnimOf(9999));
        Assert.Null(ana.PoseOf(9999));
    }

    [Fact]
    public async Task ASwingShowsUpAsAPoseTheAnimatorDraws()
    {
        var ana = await _game.JoinAsync("Ana");
        var chicken = Chicken();
        await _game.TickAndDrawAsync(ana, 2);

        ana.Attack(chicken);
        await _game.PumpAsync();
        var lifted = 0.0;
        for (var tick = 0; tick < 6; tick++)
        {
            await _game.TickAndDrawAsync(ana);
            lifted = Math.Min(lifted, LowestArm(ana));
        }

        Assert.True(lifted < -0.1);
    }

    // Frames come far faster than ticks, so this samples six of them across the tick just run.
    private double LowestArm(GameSession ana)
    {
        var tickTime = _game.Seconds;
        var lowest = 0.0;
        for (var frame = 0; frame < 6; frame++)
        {
            _game.Seconds = tickTime + (frame * 0.1);
            var pose = ana.PoseOf(InProcessGame.AvatarOf(ana));
            Assert.NotNull(pose);
            lowest = Math.Min(lowest, pose.ArmR);
        }

        _game.Seconds = tickTime;
        return lowest;
    }
}
