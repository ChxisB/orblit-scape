using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class ConversationSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private async Task<GameSession> TalkerAsync()
    {
        var ana = _game.Connect();
        ana.Login("Ana");
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana);
        var brindle = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Lamplighter, new TileCoord(33, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);
        ana.Talk(brindle);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        return ana;
    }

    [Fact]
    public async Task AConversationShowsALineThenChoicesThenEnds()
    {
        var ana = await TalkerAsync();
        Assert.Equal("Brindle", ana.Line?.Speaker);
        Assert.Empty(ana.Choices);

        ana.ContinueTalk();
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 2);
        Assert.Null(ana.Line);
        Assert.Equal(["I can help.", "Not now."], ana.Choices);

        ana.Choose(1);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 2);
        Assert.Null(ana.Line);
        Assert.Empty(ana.Choices);
    }

    [Fact]
    public async Task WalkingAwayClosesTheConversation()
    {
        var ana = await TalkerAsync();

        ana.WalkTo(new TileCoord(60, 60, 0));
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 12);

        Assert.Null(ana.Line);
        Assert.Empty(ana.Choices);
    }
}
