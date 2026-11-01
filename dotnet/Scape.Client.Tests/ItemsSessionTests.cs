using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class ItemsSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private Container Bag(string key, ContainerId id = ContainerId.Inventory) => _game.Server.Items.ContainerOf(key, id)!;

    [Fact]
    public async Task ANewSessionHoldsNothing()
    {
        var ana = await _game.JoinAsync("Ana");

        foreach (var id in Enum.GetValues<ContainerId>())
        {
            Assert.Null(ana.ItemAt(id, 0));
        }

        Assert.Empty(ana.GroundItems);
    }

    [Fact]
    public async Task AStackTheServerAddsAppearsInTheSameSlot()
    {
        var ana = await _game.JoinAsync("Ana");

        Bag("ana").Add(ItemId.Coins, 25);
        await _game.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Coins, 25), ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Null(ana.ItemAt(ContainerId.Bank, 0));
    }

    [Fact]
    public async Task AStackThatIsTakenFromShrinksThenEmpties()
    {
        var ana = await _game.JoinAsync("Ana");
        Bag("ana").Add(ItemId.Coins, 5);
        await _game.TickAsync();

        Bag("ana").Take(0, 2);
        await _game.TickAsync();
        Assert.Equal(new ItemStack(ItemId.Coins, 3), ana.ItemAt(ContainerId.Inventory, 0));

        Bag("ana").Take(0);
        await _game.TickAsync();
        Assert.Null(ana.ItemAt(ContainerId.Inventory, 0));
    }

    [Fact]
    public async Task TheBankSentInPiecesArrivesWhole()
    {
        var ana = await _game.JoinAsync("Ana");
        var bank = Bag("ana", ContainerId.Bank);
        for (var slot = 0; slot < Container.BankTabs * Container.BankTabSize; slot++)
        {
            bank.Add(ItemId.Logs, 1);
        }

        await _game.TickAsync();

        foreach (var slot in new[] { 0, 99, 100, 511, 512, 799 })
        {
            Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Bank, slot));
        }
    }

    [Fact]
    public async Task TheServerSwappingTwoSlotsIsMirrored()
    {
        var ana = await _game.JoinAsync("Ana");
        Bag("ana").Add(ItemId.Coins, 5);
        Bag("ana").Add(ItemId.Logs, 1);
        await _game.TickAsync();

        Bag("ana").Swap(0, 1);
        await _game.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Equal(new ItemStack(ItemId.Coins, 5), ana.ItemAt(ContainerId.Inventory, 1));
    }

    [Fact]
    public async Task AnotherPlayerNeverSeesTheseContainers()
    {
        await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");

        Bag("ana").Add(ItemId.Coins, 25);
        await _game.TickAsync();

        Assert.Null(bo.ItemAt(ContainerId.Inventory, 0));
    }

    [Fact]
    public async Task GroundItemsAreDrawnApartFromTheActors()
    {
        var ana = await _game.JoinAsync("Ana");
        var id = (long)_game.Server.Ground.Drop(ItemId.Logs, 3, new TileCoord(33, 32, 0));

        await _game.TickAndDrawAsync(ana);

        var sprite = Assert.Single(ana.GroundItems);
        Assert.Equal(id, sprite.NetworkId);
        Assert.Equal(ItemId.Logs, sprite.Item);
        Assert.Equal(3, sprite.Count);
        Assert.DoesNotContain(ana.Actors, actor => actor.NetworkId == id);
    }

    [Fact]
    public async Task AnItemPickedUpLeavesTheGround()
    {
        var ana = await _game.JoinAsync("Ana");
        var id = _game.Server.Ground.Drop(ItemId.Logs, 1, new TileCoord(32, 32, 0));
        await _game.TickAndDrawAsync(ana);
        Assert.Single(ana.GroundItems);

        _game.Server.Ground.PickUp("ana", id);
        await _game.TickAndDrawAsync(ana);

        Assert.Empty(ana.GroundItems);
        Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Inventory, 0));
    }

    [Fact]
    public async Task PrivateBytesThatAreNotContainerSlotsChangeNothing()
    {
        var ana = await _game.JoinAsync("Ana");
        Bag("ana").Add(ItemId.Coins, 5);
        await _game.TickAsync();

        byte tag = ItemSet.DeltaTag;
        foreach (var bytes in new[] { new byte[] { tag, 9 }, [tag, 0, 1], [tag, 0, 0, 0, 99, 0, 1, 0, 0, 0] })
        {
            _game.Server.Host.SendMessage("ana", MessageCodec.Encode(new PrivateState(bytes)));
        }

        await _game.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Coins, 5), ana.ItemAt(ContainerId.Inventory, 0));
    }
}
