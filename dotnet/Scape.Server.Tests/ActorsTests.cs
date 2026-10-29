using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class ActorsTests : IAsyncDisposable
{
    private static readonly int Head = ItemDef.EquipSlots.ToList().IndexOf("head");

    private TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private Container Of(string key, ContainerId id) => Server.Items.ContainerOf(key, id)!;

    // Look.

    [Fact]
    public async Task ANewPlayerIsSeenBareInColoursTheirNamePicks()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await _test.TickAsync();

        var seen = bo.LookOf(ana.Avatar)!;
        Assert.All(seen.Worn, item => Assert.Equal(0, item));
        Assert.Equal(seen, ana.LookOf(ana.Avatar));
    }

    [Fact]
    public async Task IsTheSameColoursAtTheNextLogin()
    {
        var ana = await _test.JoinAsync("Ana");
        var first = ana.LookOf(ana.Avatar)!;
        await _test.LeaveAsync(ana);
        await _test.TickAsync(12);

        var again = await _test.JoinAsync("Ana");

        var next = again.LookOf(again.Avatar)!;
        Assert.Equal((first.Skin, first.Hair, first.Cloth), (next.Skin, next.Hair, next.Cloth));
    }

    [Fact]
    public async Task NamesCanPickDifferentColours()
    {
        var cloths = new HashSet<int>();
        foreach (var name in new[] { "Ana", "Bo", "Cy", "Di", "Ed", "Flo" })
        {
            var client = await _test.JoinAsync(name);
            cloths.Add(client.LookOf(client.Avatar)!.Cloth);
        }

        Assert.True(cloths.Count > 1);
    }

    [Fact]
    public async Task ShowsAHelmetAnotherPlayerPutsOn()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        Of("ana", ContainerId.Inventory).Add(ItemId.BronzeHelmet, 1);
        await _test.TickAsync();

        await ana.SendAsync(new MoveItem(ContainerId.Inventory, 0, ContainerId.Equipment, Head));
        await _test.TickAsync(2);

        Assert.Equal(ItemId.BronzeHelmet, bo.LookOf(ana.Avatar)!.Worn[Head]);
        Assert.Equal(0, bo.LookOf(bo.Avatar)!.Worn[Head]);
    }

    [Fact]
    public async Task DropsTheHelmetWhenItComesOff()
    {
        var ana = await _test.JoinAsync("Ana");
        var worn = Of("ana", ContainerId.Equipment);
        worn.Add(ItemId.BronzeHelmet, 1);
        await _test.TickAsync(2);
        Assert.Equal(ItemId.BronzeHelmet, ana.LookOf(ana.Avatar)!.Worn[Head]);

        worn.Take(Head);
        await _test.TickAsync(2);

        Assert.Equal(0, ana.LookOf(ana.Avatar)!.Worn[Head]);
    }

    // A creature out of the phase.

    [Fact]
    public async Task ACreatureOutOfThePhaseHasNoStepsLeftOverFromWhenItLastWalked()
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Npcs = new NpcSet([new NpcDef(1, "Hen", new Wander())]) });
        var ana = await _test.JoinAsync("Ana");
        var chicken = Server.Npcs.Spawn(new NpcSpawn(1, new TileCoord(40, 32, 0), radius: 20));
        for (var i = 0; i < 10 && chicken.StepsLastTick == 0; i++)
        {
            await _test.TickAsync();
        }

        Assert.Equal(1, chicken.StepsLastTick);

        Server.Movement.Teleport("ana", new TileCoord(100, 32, 0));

        // A cell stays simulated for ten ticks after the last player leaves. The walk is long
        // enough that it never ends before the creature sleeps.
        await _test.TickAsync(14);

        Assert.Equal(0, chicken.StepsLastTick);
        Assert.Null(ana.AnimOf((long)chicken.NetworkId));
    }

    // Resting state.

    [Fact]
    public async Task IsIdleForAPlayerWhoStands()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.TickAsync(2);

        Assert.Equal(new Anim(), ana.AnimOf(ana.Avatar));
    }

    [Fact]
    public async Task IsWalkWhileTheyTakeOneStepATickThenIdle()
    {
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(new Walk(new TileCoord(34, 32, 0)));
        await _test.TickAsync();
        Assert.Equal(ActorState.Walk, ana.AnimOf(ana.Avatar)!.Base);

        await _test.TickAsync(4);

        Assert.Equal(ActorState.Idle, ana.AnimOf(ana.Avatar)!.Base);
    }

    // Acts.

    /// <summary>What <paramref name="viewer"/> sees <paramref name="networkId"/> doing after each of the next <paramref name="ticks"/> ticks.</summary>
    private async Task<List<Anim>> WatchAsync(TestClient viewer, long networkId, int ticks)
    {
        var seen = new List<Anim>();
        for (var i = 0; i < ticks; i++)
        {
            await _test.TickAsync();
            if (viewer.AnimOf(networkId) is { } anim)
            {
                seen.Add(anim);
            }
        }

        return seen;
    }

    [Fact]
    public async Task ChoppingATreePlaysGather()
    {
        var ana = await _test.JoinAsync("Ana");
        Of("ana", ContainerId.Inventory).Add(ItemId.BronzeAxe, 1);
        var tree = Server.Objects.Place(ObjectId.Tree, new TileCoord(33, 32, 0));

        await ana.ClickAsync(InteractKind.PlacedObject, (long)tree.NetworkId);

        var seen = await WatchAsync(ana, ana.Avatar, Gathering.Ticks + 2);
        Assert.Contains(ActorState.Gather, seen.Select(anim => anim.Act));
    }

    [Fact]
    public async Task CookingAtAFirePlaysMake()
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Recipes = DefaultRecipes.All });
        var ana = await _test.JoinAsync("Ana");
        Of("ana", ContainerId.Inventory).Add(ItemId.RawFish, 1);
        var fire = Server.Objects.Place(ObjectId.Fire, new TileCoord(33, 32, 0));

        await ana.ClickAsync(InteractKind.UseOnObject, (long)fire.NetworkId);

        var seen = await WatchAsync(ana, ana.Avatar, 6);
        Assert.Contains(ActorState.Make, seen.Select(anim => anim.Act));
    }

    [Fact]
    public async Task AClickOnATreeWithNoAxePlaysNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        var tree = Server.Objects.Place(ObjectId.Tree, new TileCoord(33, 32, 0));

        await ana.ClickAsync(InteractKind.PlacedObject, (long)tree.NetworkId);

        var seen = await WatchAsync(ana, ana.Avatar, Gathering.Ticks + 2);
        Assert.All(seen, anim => Assert.Null(anim.Act));
    }
}
