using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class SkillsSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task ANewSessionHoldsNoExperienceAndIsLevelOne()
    {
        var ana = await _game.JoinAsync("Ana");

        Assert.Equal(0, ana.XpOf("mining"));
        Assert.Equal(1, ana.LevelOf("mining"));
    }

    [Fact]
    public async Task ExperienceTheServerGrantsReachesTheOwnerAndNoOneElse()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        var enough = DefaultSkills.All.Curve.XpForLevel(5) + 3;

        _game.Server.Skills.Grant("ana", "mining", enough);
        await _game.TickAsync();

        Assert.Equal(enough, ana.XpOf("mining"));
        Assert.Equal(5, ana.LevelOf("mining"));
        Assert.Equal(0, ana.XpOf("fishing"));
        Assert.Equal(0, bo.XpOf("mining"));
    }

    [Fact]
    public async Task LaterGrantsAddToWhatTheClientHolds()
    {
        var ana = await _game.JoinAsync("Ana");

        _game.Server.Skills.Grant("ana", "fishing", 40);
        await _game.TickAsync();
        _game.Server.Skills.Grant("ana", "mining", 70);
        _game.Server.Skills.Grant("ana", "fishing", 2);
        await _game.TickAsync();

        Assert.Equal(42, ana.XpOf("fishing"));
        Assert.Equal(70, ana.XpOf("mining"));
    }

    [Fact]
    public async Task ASkillTheGameDoesNotHaveIsRefused()
    {
        var ana = await _game.JoinAsync("Ana");

        Assert.Throws<ArgumentException>(() => ana.XpOf("dancing"));
        Assert.Throws<ArgumentException>(() => ana.LevelOf("dancing"));
    }

    [Fact]
    public async Task PrivateBytesOfAnotherKindOrOfNoShapeChangeNothing()
    {
        var ana = await _game.JoinAsync("Ana");
        _game.Server.Skills.Grant("ana", "fishing", 40);
        await _game.TickAsync();

        byte tag = SkillSet.DeltaTag;
        foreach (var bytes in new[] { Array.Empty<byte>(), [(byte)(tag + 1), 0, 0], [tag, 9] })
        {
            _game.Server.Host.SendMessage("ana", MessageCodec.Encode(new PrivateState(bytes)));
        }

        await _game.TickAsync();

        Assert.Equal(40, ana.XpOf("fishing"));
    }
}
