using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class CombatSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private long Spawn(int npc) => (long)_game.Server.Npcs.Spawn(new NpcSpawn(npc, new TileCoord(33, 32, 0))).NetworkId;

    [Fact]
    public async Task AFighterAndACreatureInViewShowTheirHitPoints()
    {
        var ana = await _game.JoinAsync("Ana");
        var chicken = Spawn(NpcId.Chicken);

        await _game.TickAndDrawAsync(ana, 2);

        Assert.Equal(new HitPoints(10, 10), ana.HealthOf(InProcessGame.AvatarOf(ana)));
        Assert.Equal(new HitPoints(3, 3), ana.HealthOf(chicken));
    }

    [Fact]
    public async Task SomethingThatCannotFightHasNoHitPoints()
    {
        var ana = await _game.JoinAsync("Ana");
        var guard = Spawn(NpcId.Guard);

        await _game.TickAndDrawAsync(ana, 2);

        Assert.Null(ana.HealthOf(guard));
    }

    [Fact]
    public async Task AnAttackSendsTheClickAndTheSwingsComeBack()
    {
        var ana = await _game.JoinAsync("Ana");
        var chicken = Spawn(NpcId.Chicken);
        await _game.TickAndDrawAsync(ana, 2);

        ana.Attack(chicken);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);

        Assert.NotEmpty(ana.Splats);
        Assert.Equal(chicken, ana.Splats[0].Target);
        Assert.InRange(ana.Splats[0].Damage, 0, 2);
    }

    [Fact]
    public async Task AWoundedFighterShowsWhatIsLeftOfTheirHitPoints()
    {
        var ana = await _game.JoinAsync("Ana");
        var avatar = InProcessGame.AvatarOf(ana);
        Spawn(NpcId.Goblin);

        await _game.TickAndDrawAsync(ana, 8);

        var taken = ana.Splats.Where(splat => splat.Target == avatar).Sum(splat => splat.Damage);
        Assert.True(taken > 0);
        Assert.Equal(new HitPoints(10 - taken, 10), ana.HealthOf(avatar));
    }
}
