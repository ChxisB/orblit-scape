using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class ObjectsSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task AnObjectInViewIsListedWithItsStateAndIsNotAnActor()
    {
        var ana = await _game.JoinAsync("Ana");
        var door = _game.Server.Objects.Place(ObjectId.Door, new TileCoord(36, 32, 0));

        await _game.TickAndDrawAsync(ana);

        var sprite = Assert.Single(ana.Objects);
        Assert.Equal((long)door.NetworkId, sprite.NetworkId);
        Assert.Equal(ObjectId.Door, sprite.Def);
        Assert.Equal(0, sprite.State);
        Assert.Equal((36.0, 32.0, 0), (sprite.X, sprite.Y, sprite.Plane));
        Assert.Single(ana.Actors);
        Assert.Empty(ana.GroundItems);
    }

    [Fact]
    public async Task AStateChangeReachesTheDrawing()
    {
        var ana = await _game.JoinAsync("Ana");
        var door = _game.Server.Objects.Place(ObjectId.Door, new TileCoord(36, 32, 0));
        await _game.TickAndDrawAsync(ana);

        _game.Server.Objects.SetState(door, 1);
        await _game.TickAndDrawAsync(ana, 2);

        Assert.Equal(1, Assert.Single(ana.Objects).State);
    }

    [Fact]
    public async Task PickingAnOptionSendsTheClickAndTheDoorOpens()
    {
        var ana = await _game.JoinAsync("Ana");
        var door = _game.Server.Objects.Place(ObjectId.Door, new TileCoord(36, 32, 0));
        await _game.TickAndDrawAsync(ana);

        ana.UseObject((long)door.NetworkId, 0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);

        Assert.Equal(1, door.State);
        Assert.Equal(1, Assert.Single(ana.Objects).State);
    }

    [Fact]
    public async Task TakingAnItemFromTheGroundPutsItInTheInventory()
    {
        var ana = await _game.JoinAsync("Ana");
        var id = _game.Server.Ground.Drop(ItemId.Logs, 1, new TileCoord(34, 32, 0));
        await _game.TickAndDrawAsync(ana);

        ana.TakeItem((long)id);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 4);

        Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Empty(ana.GroundItems);
    }

    [Fact]
    public async Task BothUseSendersReachTheServerHandlers()
    {
        var ana = await _game.JoinAsync("Ana");
        var door = _game.Server.Objects.Place(ObjectId.Door, new TileCoord(34, 32, 0));
        var used = new List<string>();
        _game.Server.Interactions.AddUseOnObject(ItemId.Logs, ObjectId.Door, (_, slot, _) => used.Add($"object {slot}"));
        _game.Server.Interactions.AddUseOnItem(ItemId.Logs, ItemId.Coins, (_, slot, onSlot) => used.Add($"item {slot} {onSlot}"));
        var bag = _game.Server.Items.ContainerOf("ana", ContainerId.Inventory)!;
        bag.Add(ItemId.Logs, 1);
        bag.Add(ItemId.Coins, 3);
        await _game.TickAndDrawAsync(ana);

        ana.UseItemOnItem(0, 1);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana);
        ana.UseItemOnObject(0, (long)door.NetworkId);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 4);

        Assert.Equal(["item 0 1", "object 0"], used);
    }
}
