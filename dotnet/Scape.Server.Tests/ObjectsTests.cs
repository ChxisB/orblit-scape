using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class ObjectsTests : IAsyncDisposable
{
    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    private TileMap Map => _test.Map;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    // Placing.

    [Fact]
    public async Task APlacedObjectIsSeenWithItsDefinitionAndState()
    {
        var ana = await _test.JoinAsync("Ana");
        var door = Server.Objects.Place(ObjectId.Door, At(36, 32));
        await _test.TickAsync();

        var stairs = Server.Objects.Place(ObjectId.StairsDown, At(38, 32));
        await _test.TickAsync();

        Assert.Equal(
            new Dictionary<long, (int, int)>
            {
                [(long)door.NetworkId] = (ObjectId.Door, 0),
                [(long)stairs.NetworkId] = (ObjectId.StairsDown, 0),
            },
            ana.Objects);
        Assert.Equal(At(36, 32), ana.TileOf((long)door.NetworkId));
        Assert.Same(door, Server.Objects.Find(door.NetworkId));
        Assert.Same(door, Server.Objects.At(At(36, 32)));
        Assert.Null(Server.Objects.At(At(37, 32)));
    }

    [Fact]
    public async Task AnObjectIsNotAnActorAndThePlayerIsNotAnObject()
    {
        var ana = await _test.JoinAsync("Ana");
        Server.Objects.Place(ObjectId.Door, At(36, 32));
        await _test.TickAsync();

        Assert.Equal(2, ana.Client.EntityCount);
        Assert.Single(ana.Objects);
    }

    [Fact]
    public void ASolidObjectBlocksItsTileAndAStairDoesNot()
    {
        Server.Objects.Place(ObjectId.Door, At(36, 32));
        Server.Objects.Place(ObjectId.StairsUp, At(38, 32));

        Assert.True(Map.FlagsAt(At(36, 32)).Has(TileFlags.Blocked));
        Assert.False(Map.FlagsAt(At(38, 32)).Has(TileFlags.Blocked));
    }

    [Fact]
    public void ATileThatIsBlockedWaterOrTakenRefusesAnObject()
    {
        Map.SetFlags(At(40, 40), TileFlags.Blocked);
        Map.SetFlags(At(42, 40), TileFlags.Water);
        Server.Objects.Place(ObjectId.StairsUp, At(44, 40));

        foreach (var tile in new[] { At(40, 40), At(42, 40), At(44, 40) })
        {
            Assert.Throws<ArgumentException>(() => Server.Objects.Place(ObjectId.StairsDown, tile));
        }
    }

    [Fact]
    public void AnObjectThatDoesNotExistCannotBePlaced() =>
        Assert.Throws<ArgumentException>(() => Server.Objects.Place(999, At(36, 32)));

    // State.

    [Fact]
    public async Task ADoorThatOpensFreesItsTileAndTheClientIsTold()
    {
        var ana = await _test.JoinAsync("Ana");
        var door = Server.Objects.Place(ObjectId.Door, At(36, 32));
        await _test.TickAsync();

        Server.Objects.SetState(door, 1);
        await _test.TickAsync();

        Assert.Equal(1, door.State);
        Assert.False(Map.FlagsAt(At(36, 32)).Has(TileFlags.Blocked));
        Assert.Equal((ObjectId.Door, 1), ana.Objects[(long)door.NetworkId]);

        Server.Objects.SetState(door, 0);
        await _test.TickAsync();

        Assert.True(Map.FlagsAt(At(36, 32)).Has(TileFlags.Blocked));
        Assert.Equal((ObjectId.Door, 0), ana.Objects[(long)door.NetworkId]);
    }

    [Fact]
    public void AStateOutsideTheInt32RangeIsRefused()
    {
        var door = Server.Objects.Place(ObjectId.Door, At(36, 32));

        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Objects.SetState(door, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Objects.SetState(door, 0x80000000));
        Server.Objects.SetState(door, 0x7FFFFFFF);
        Assert.Equal(0x7FFFFFFF, door.State);
    }

    [Fact]
    public void OpeningADoorLeavesAWallFlagBesideItAlone()
    {
        Map.SetFlags(At(36, 32), TileFlags.WallNorth);
        var door = Server.Objects.Place(ObjectId.Door, At(36, 32));
        Server.Objects.SetState(door, 1);

        Assert.True(Map.FlagsAt(At(36, 32)).Has(TileFlags.WallNorth));
    }
}
