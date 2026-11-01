using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class NpcsSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task ACreatureInViewIsListedWithItsKindAndIsNotAnActor()
    {
        var ana = await _game.JoinAsync("Ana");
        var guard = _game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Guard, new TileCoord(36, 32, 0)));

        await _game.TickAndDrawAsync(ana, 2);

        var sprite = Assert.Single(ana.Npcs);
        Assert.Equal((long)guard.NetworkId, sprite.NetworkId);
        Assert.Equal(NpcId.Guard, sprite.Def);
        Assert.Equal((36.0, 32.0, 0), (sprite.X, sprite.Y, sprite.Plane));
        Assert.Single(ana.Actors);
        Assert.Empty(ana.Objects);
        Assert.Empty(ana.GroundItems);
    }
}
