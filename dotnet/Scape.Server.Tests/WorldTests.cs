using Scape.Content;
using Scape.Content.Tests;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class WorldTests : IAsyncDisposable
{
    private readonly WorldPlan _world = TestWorld.Build();
    private readonly TestServer _test;

    public WorldTests() =>
        _test = new TestServer(options => options with { Map = _world.Map, Spawn = _world.Spawn, Recipes = DefaultRecipes.All });

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private void Stock() => Server.Stock(_world.Objects, _world.Npcs);

    [Fact]
    public void TheServerTakesInEveryObjectAndCreatureOfTheWorld()
    {
        Stock();

        Assert.Equal(_world.Npcs.Count, Server.Npcs.Count);
        Assert.All(_world.Objects, placement =>
        {
            var placed = Server.Objects.At(placement.Tile);
            Assert.NotNull(placed);
            Assert.Equal(placement.Destination, placed.Destination);
        });
    }

    [Fact]
    public async Task ANewPlayerAppearsOnTheSpawnTileOfTheWorld()
    {
        Stock();

        await _test.JoinAsync("Ana");

        Assert.Equal(_world.Spawn, Server.Movement.TileOf("ana"));
    }

    [Fact]
    public async Task APlayerOpensTheShopDoorAndWalksInToTheShopkeeper()
    {
        Stock();
        var door = Server.Objects.At(TestWorld.ShopDoor)!;
        var ana = await _test.JoinAsync("Ana");
        await _test.TickAsync();

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync(40);
        await ana.SendAsync(new Walk(At(22, 26)));
        await _test.TickAsync(12);

        Assert.Equal(1, door.State);
        Assert.Equal(At(22, 26), Server.Movement.TileOf("ana"));
    }

    [Fact]
    public async Task APlayerWalksToTheFurnaceAndSmeltsABar()
    {
        Stock();
        var furnace = Server.Objects.At(TestWorld.Furnace)!;
        var ana = await _test.JoinAsync("Ana");
        var bag = Server.Items.ContainerOf("ana", ContainerId.Inventory)!;
        bag.Add(ItemId.CopperOre, 1);

        await ana.SendAsync(new Walk(At(11, 20)));
        await _test.TickAsync(20);
        await ana.ClickAsync(InteractKind.UseOnObject, (long)furnace.NetworkId);
        await _test.TickAsync(6);

        Assert.Equal(1, bag.CountOf(ItemId.BronzeBar));
        Assert.Equal(0, bag.CountOf(ItemId.CopperOre));
    }

    [Fact]
    public void APlacementTheWorldRefusesIsAnError() =>
        Assert.Throws<ArgumentException>(() => Server.Stock([new ObjectPlacement(ObjectId.Rock, TestWorld.Sea)], []));
}
