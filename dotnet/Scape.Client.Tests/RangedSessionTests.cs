using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class RangedSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task ACastSendsTheClickAndTheFlightComesBack()
    {
        var ana = await _game.JoinAsync("Ana");
        _game.Server.Items.ContainerOf("ana", ContainerId.Inventory)!.Add(ItemId.Runes, 3);
        var chicken = (long)_game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, new TileCoord(35, 32, 0))).NetworkId;
        await _game.TickAndDrawAsync(ana, 2);

        ana.Cast(SpellId.Strike, chicken);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);

        var shot = Assert.Single(ana.Shots);
        Assert.Equal(chicken, shot.To);
        Assert.Equal(InProcessGame.AvatarOf(ana), shot.From);
        Assert.Equal(CombatStyle.Magic, shot.Style);
    }

    [Fact]
    public async Task APrayerToggleSendsTheClickAndThePointsComeBack()
    {
        var ana = await _game.JoinAsync("Ana");
        await _game.TickAndDrawAsync(ana, 2);
        Assert.Empty(ana.Prayer!.Active);

        ana.TogglePrayer(PrayerId.ThickSkin);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 2);

        Assert.Equal(1, ana.Prayer!.Points);
        Assert.Equal([PrayerId.ThickSkin], ana.Prayer!.Active);
    }
}
