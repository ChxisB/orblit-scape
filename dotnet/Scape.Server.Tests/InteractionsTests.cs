using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class InteractionsTests : IAsyncDisposable
{
    private const string CannotReach = "You cannot reach that.";
    private const string NothingHappens = "Nothing interesting happens.";

    private static readonly TileCoord Spawn = OpenWorld.SpawnTile;

    private readonly TestServer _test = new();
    private readonly List<(string Player, int Slot, long On)> _used = [];

    public InteractionsTests() => _test.Map.AddRegion(FlagRegion.Open(new RegionKey(0, 0, 1)));

    private ScapeServer Server => _test.Server;

    private TileMap Map => _test.Map;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static TileCoord Where(TestClient client) => client.TileOf(client.Avatar)!.Value;

    private Container Inventory(string key) => Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    /// <summary>A door at (40, 32) in a wall that runs the height of the world.</summary>
    private PlacedObject DoorInAWall()
    {
        var door = Server.Objects.Place(ObjectId.Door, At(40, 32));
        for (var y = 0; y < 128; y++)
        {
            if (y != 32)
            {
                Map.SetFlags(At(40, y), TileFlags.Blocked);
            }
        }

        return door;
    }

    // A door.

    [Fact]
    public async Task ADoorIsAWallUntilOpenedThenThePlayerWalksThrough()
    {
        var door = DoorInAWall();
        var ana = await _test.JoinAsync("Ana");
        Assert.False(PathSearch.Find(Map, Spawn, At(48, 32)).Reached);

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync(10);

        Assert.Equal(1, door.State);
        Assert.Equal(At(39, 32), Where(ana));
        Assert.True(PathSearch.Find(Map, At(39, 32), At(48, 32)).Reached);

        await ana.SendAsync(new Walk(At(48, 32)));
        await _test.TickAsync(12);

        Assert.Equal(At(48, 32), Where(ana));
    }

    [Fact]
    public async Task ADoorClosesAgainWithItsSecondOption()
    {
        var door = DoorInAWall();
        var ana = await _test.JoinAsync("Ana");
        Server.Objects.SetState(door, 1);

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId, 1);
        await _test.TickAsync(10);

        Assert.Equal(0, door.State);
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task ADoorOffersOnlyTheOptionThatFitsItsState()
    {
        var door = DoorInAWall();
        var ana = await _test.JoinAsync("Ana");
        Server.Objects.SetState(door, 1);

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync(10);

        Assert.Equal(1, door.State);
        Assert.Equal([NothingHappens], ana.Notices);
    }

    [Fact]
    public async Task AnOptionOrAnObjectThatIsNotThereIsIgnored()
    {
        var door = DoorInAWall();
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId, 2);
        await ana.ClickAsync(InteractKind.PlacedObject, 99999);
        await _test.TickAsync(10);

        Assert.Equal(0, door.State);
        Assert.Empty(ana.Notices);
        Assert.Equal(Spawn, Where(ana));
    }

    [Fact]
    public async Task AClickBesideADoorOpensItWithoutAWalk()
    {
        var door = DoorInAWall();
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(new Walk(At(39, 32)));
        await _test.TickAsync(8);

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync();

        Assert.Equal(1, door.State);
    }

    // Cancelling.

    [Fact]
    public async Task AWalkBeforeThePlayerArrivesMeansNothingHappens()
    {
        var door = Server.Objects.Place(ObjectId.Door, At(40, 32));
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync(2);
        await ana.SendAsync(new Walk(At(30, 32)));
        await _test.TickAsync(12);

        Assert.Equal(0, door.State);
        Assert.Equal(At(30, 32), Where(ana));
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task ASecondClickReplacesTheFirst()
    {
        var first = Server.Objects.Place(ObjectId.Door, At(40, 32));
        var second = Server.Objects.Place(ObjectId.Door, At(32, 40));
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)first.NetworkId);
        await _test.TickAsync(2);
        await ana.ClickAsync(InteractKind.PlacedObject, (long)second.NetworkId);
        await _test.TickAsync(14);

        Assert.Equal(0, first.State);
        Assert.Equal(1, second.State);
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task AClickWhileAlreadyInReachStopsTheWalkUnderWay()
    {
        var door = Server.Objects.Place(ObjectId.Door, At(31, 34));
        var ana = await _test.JoinAsync("Ana");
        await ana.SendAsync(new Walk(At(32, 40)));
        await _test.TickAsync(2);
        var stoodAt = Where(ana);

        await ana.ClickAsync(InteractKind.PlacedObject, (long)door.NetworkId);
        await _test.TickAsync(8);

        Assert.Equal(1, door.State);
        Assert.Equal(stoodAt, Where(ana));
    }

    // Unreachable.

    [Fact]
    public async Task AnObjectWithNoWayUpToItSaysSoAndThePlayerStays()
    {
        var stairs = Server.Objects.Place(ObjectId.StairsUp, At(50, 50));
        foreach (var (dx, dy) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) })
        {
            Map.SetFlags(At(50 + dx, 50 + dy), TileFlags.Blocked);
        }

        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)stairs.NetworkId);
        await _test.TickAsync(3);

        Assert.Equal([CannotReach], ana.Notices);
        Assert.Equal(Spawn, Where(ana));
    }

    [Fact]
    public async Task AnObjectOnAnotherPlaneCannotBeReached()
    {
        var stairs = Server.Objects.Place(ObjectId.StairsUp, new TileCoord(34, 32, 1));
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)stairs.NetworkId);
        await _test.TickAsync(3);

        Assert.Equal([CannotReach], ana.Notices);
    }

    // Stairs.

    [Fact]
    public async Task ClimbingPutsThePlayerOnTheDestinationPlane()
    {
        var stairs = Server.Objects.Place(ObjectId.StairsUp, At(34, 32), destination: new TileCoord(36, 36, 1));
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)stairs.NetworkId);
        await _test.TickAsync(6);

        Assert.Equal(new TileCoord(36, 36, 1), Where(ana));

        await _test.TickAsync(3);

        Assert.Equal(new TileCoord(36, 36, 1), Where(ana));
    }

    [Fact]
    public async Task StairsThatLeadNowhereDoNothing()
    {
        var stairs = Server.Objects.Place(ObjectId.StairsDown, At(34, 32));
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.PlacedObject, (long)stairs.NetworkId);
        await _test.TickAsync(6);

        Assert.Equal([NothingHappens], ana.Notices);
        Assert.Equal(At(33, 32), Where(ana));
    }

    // Picking up from the ground.

    [Fact]
    public async Task ThePlayerWalksToTheItemAndTakesIt()
    {
        var ana = await _test.JoinAsync("Ana");
        var id = Server.Ground.Drop(ItemId.Logs, 1, At(36, 32));
        await _test.TickAsync();

        await ana.ClickAsync(InteractKind.Ground, (long)id);
        await _test.TickAsync(8);

        Assert.Equal(new ItemStack(ItemId.Logs, 1), Inventory("ana").At(0));
        Assert.Empty(ana.GroundItems);
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task AFullInventoryLeavesTheItemAndSaysSo()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize);
        var id = Server.Ground.Drop(ItemId.Logs, 1, At(34, 32));
        await _test.TickAsync();

        await ana.ClickAsync(InteractKind.Ground, (long)id);
        await _test.TickAsync(6);

        Assert.Equal(["You have no room for that."], ana.Notices);
        Assert.Equal([(long)id], ana.GroundItems.Keys);
    }

    [Fact]
    public async Task AnItemTakenByAnotherBeforeArrivalIsGone()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await bo.SendAsync(new Walk(At(34, 32)));
        await _test.TickAsync(3);
        var id = Server.Ground.Drop(ItemId.Logs, 1, At(34, 32));
        await _test.TickAsync();

        await ana.ClickAsync(InteractKind.Ground, (long)id);
        await _test.TickAsync();
        Assert.Equal(Outcome.Done, Server.Ground.PickUp("bo", id));
        await _test.TickAsync(5);

        Assert.Equal(["It is gone."], ana.Notices);
        Assert.Null(Inventory("ana").At(0));
    }

    [Fact]
    public async Task AnItemThePlayerCannotSeeIsNotWalkedTo()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        var id = Server.Ground.Drop(ItemId.Logs, 1, At(36, 32), owner: "bo");
        await _test.TickAsync();

        await ana.ClickAsync(InteractKind.Ground, (long)id);
        await _test.TickAsync(8);

        Assert.Equal(Spawn, Where(ana));
        Assert.Empty(ana.Notices);
        Assert.Null(Inventory("ana").At(0));
    }

    // Using an item on an object.

    /// <summary>A door at (36, 32) that logs can be used on, each use noted in <see cref="_used"/>.</summary>
    private PlacedObject DoorTakingLogs()
    {
        var door = Server.Objects.Place(ObjectId.Door, At(36, 32));
        Server.Interactions.AddUseOnObject(
            ItemId.Logs,
            ObjectId.Door,
            (player, slot, placed) => _used.Add((player, slot, (long)placed.NetworkId)));
        return door;
    }

    [Fact]
    public async Task UsingAnItemOnAnObjectRunsWhatIsRegisteredOnceThePlayerIsBesideIt()
    {
        var door = DoorTakingLogs();
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);
        Inventory("ana").Add(ItemId.Logs, 1);

        await ana.ClickAsync(InteractKind.UseOnObject, (long)door.NetworkId, 1);
        await _test.TickAsync(6);

        Assert.Equal([("ana", 1, (long)door.NetworkId)], _used);
        Assert.Equal(At(35, 32), Where(ana));
    }

    [Fact]
    public async Task AnItemWithNoUseOnThatObjectDoesNothing()
    {
        var door = DoorTakingLogs();
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);

        await ana.ClickAsync(InteractKind.UseOnObject, (long)door.NetworkId);
        await _test.TickAsync(6);

        Assert.Empty(_used);
        Assert.Equal([NothingHappens], ana.Notices);
    }

    [Fact]
    public async Task UsingAnEmptySlotOnAnObjectIsIgnored()
    {
        var door = DoorTakingLogs();
        var ana = await _test.JoinAsync("Ana");

        await ana.ClickAsync(InteractKind.UseOnObject, (long)door.NetworkId, 5);
        await _test.TickAsync(6);

        Assert.Empty(_used);
        Assert.Empty(ana.Notices);
        Assert.Equal(Spawn, Where(ana));
    }

    [Fact]
    public void RegisteringTheSameUseOnAnObjectTwiceThrows()
    {
        DoorTakingLogs();

        Assert.Throws<ArgumentException>(() => Server.Interactions.AddUseOnObject(ItemId.Logs, ObjectId.Door, (_, _, _) => { }));
    }

    // Using an item on an item.

    /// <summary>Logs can be used on coins, each use noted in <see cref="_used"/>.</summary>
    private void LogsOnCoins() =>
        Server.Interactions.AddUseOnItem(ItemId.Logs, ItemId.Coins, (player, slot, onSlot) => _used.Add((player, slot, onSlot)));

    private async Task<TestClient> AnaWithLogsThenCoinsAsync()
    {
        LogsOnCoins();
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, 1);
        Inventory("ana").Add(ItemId.Coins, 5);
        return ana;
    }

    [Fact]
    public async Task UsingAnItemOnAnItemRunsAtOnceWithTheFirstItemInTheFirstSlot()
    {
        var ana = await AnaWithLogsThenCoinsAsync();

        await ana.ClickAsync(InteractKind.UseOnItem, 1, 0);
        await _test.TickAsync();

        Assert.Equal([("ana", 0, 1L)], _used);
        Assert.Equal(Spawn, Where(ana));
    }

    [Fact]
    public async Task UsingAnItemOnAnItemWorksWhicheverIsUsedOnTheOther()
    {
        var ana = await AnaWithLogsThenCoinsAsync();

        await ana.ClickAsync(InteractKind.UseOnItem, 0, 1);
        await _test.TickAsync();

        Assert.Equal([("ana", 0, 1L)], _used);
    }

    [Fact]
    public async Task AnItemOnItselfOrAnUnmatchedPairDoesNothing()
    {
        var ana = await AnaWithLogsThenCoinsAsync();
        Inventory("ana").Add(ItemId.BronzeHelmet, 1);

        await ana.ClickAsync(InteractKind.UseOnItem, 0, 0);
        await _test.TickAsync();
        await ana.ClickAsync(InteractKind.UseOnItem, 2, 0);
        await _test.TickAsync();

        Assert.Empty(_used);
        Assert.Equal([NothingHappens, NothingHappens], ana.Notices);
    }

    [Fact]
    public async Task AnItemIsNeverUsedOnItsOwnSlot()
    {
        LogsOnCoins();
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, 2);
        Server.Interactions.AddUseOnItem(ItemId.Logs, ItemId.Logs, (player, slot, onSlot) => _used.Add((player, slot, onSlot)));

        await ana.ClickAsync(InteractKind.UseOnItem, 0, 0);
        await _test.TickAsync();
        Assert.Empty(_used);

        await ana.ClickAsync(InteractKind.UseOnItem, 1, 0);
        await _test.TickAsync();
        Assert.Equal([("ana", 0, 1L)], _used);
    }

    [Fact]
    public async Task AStaleSlotDoesNothing()
    {
        var ana = await AnaWithLogsThenCoinsAsync();

        await ana.ClickAsync(InteractKind.UseOnItem, 9, 0);
        await _test.TickAsync();

        Assert.Empty(_used);
        Assert.Equal([NothingHappens], ana.Notices);
    }

    [Fact]
    public void RegisteringAnItemPairAgainInEitherOrderThrows()
    {
        LogsOnCoins();

        foreach (var (a, b) in new[] { (ItemId.Logs, ItemId.Coins), (ItemId.Coins, ItemId.Logs) })
        {
            Assert.Throws<ArgumentException>(() => Server.Interactions.AddUseOnItem(a, b, (_, _, _) => { }));
        }
    }

    [Fact]
    public void AnActionNameCanBeRegisteredOnce() =>
        Assert.Throws<ArgumentException>(() => Server.Interactions.AddObjectAction("toggle", (_, _) => { }));
}
