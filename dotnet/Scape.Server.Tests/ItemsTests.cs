using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class ItemsTests : IAsyncDisposable
{
    private static readonly TileCoord Spawn = OpenWorld.SpawnTile;

    private readonly TestServer _test = new();

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static MoveItem Drag(ContainerId from, int a, ContainerId to, int b) => new(from, a, to, b);

    private Container Inventory(string key) => Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    private Container Of(string key, ContainerId id) => Server.Items.ContainerOf(key, id)!;

    // Containers.

    [Fact]
    public async Task ANewPlayerHasAnEmptyInventoryEquipmentAndBank()
    {
        var ana = await _test.JoinAsync("Ana");

        foreach (var id in Enum.GetValues<ContainerId>())
        {
            Assert.Empty(Of("ana", id).Dirty);
        }

        Assert.Empty(ana.ItemDeltas);
    }

    [Fact]
    public async Task EachContainerHasItsOwnShape()
    {
        await _test.JoinAsync("Ana");

        Assert.Equal(Container.InventorySize, Of("ana", ContainerId.Inventory).Size);
        Assert.Equal(Container.BankTabs * Container.BankTabSize, Of("ana", ContainerId.Bank).Size);
        Assert.Equal(ItemDef.EquipSlots.Count, Of("ana", ContainerId.Equipment).Size);
        Assert.Equal(0, Of("ana", ContainerId.Equipment).Add(ItemId.Logs, 1));
        Assert.Equal(1, Of("ana", ContainerId.Equipment).Add(ItemId.BronzeHelmet, 1));
    }

    [Fact]
    public async Task AChangeIsSentAsOneDeltaNamingTheSlotsThatChanged()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 50);
        Inventory("ana").Add(ItemId.Logs, 1);

        await _test.TickAsync();

        var delta = Assert.Single(ana.ItemDeltas);
        Assert.Equal(ContainerId.Inventory, delta.Container);
        Assert.Equal(
            [new SlotChange(0, new ItemStack(ItemId.Coins, 50)), new SlotChange(1, new ItemStack(ItemId.Logs, 1))],
            delta.Changes);
    }

    [Fact]
    public async Task ATickWithNoChangeSendsNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);
        await _test.TickAsync();

        await _test.TickAsync(3);

        Assert.Single(ana.ItemDeltas);
    }

    [Fact]
    public async Task ASlotChangedTwiceInATickIsSentOnceAsItEnds()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);
        Inventory("ana").Add(ItemId.Coins, 5);

        await _test.TickAsync();

        Assert.Equal([new SlotChange(0, new ItemStack(ItemId.Coins, 10))], Assert.Single(ana.ItemDeltas).Changes);
    }

    [Fact]
    public async Task AChangeToTheBankNamesTheBank()
    {
        var ana = await _test.JoinAsync("Ana");
        Of("ana", ContainerId.Bank).Add(ItemId.Logs, 1);

        await _test.TickAsync();

        Assert.Equal(ContainerId.Bank, Assert.Single(ana.ItemDeltas).Container);
    }

    [Fact]
    public async Task OnlyTheOwnerIsTold()
    {
        await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        Inventory("ana").Add(ItemId.Coins, 5);

        await _test.TickAsync();

        Assert.Empty(bo.ItemDeltas);
    }

    [Fact]
    public async Task APlayerWhoLeavesHasNoContainers()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.LeaveAsync(ana);

        await _test.TickAsync(12);

        Assert.Null(Server.Items.ContainerOf("ana", ContainerId.Inventory));
    }

    [Fact]
    public void APlayerWhoIsNotInTheWorldHasNone() =>
        Assert.Null(Server.Items.ContainerOf("nobody", ContainerId.Bank));

    // Moving an item.

    [Fact]
    public async Task SwapsTwoInventorySlotsAndTellsTheOwner()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);
        Inventory("ana").Add(ItemId.Logs, 1);
        await _test.TickAsync();

        await ana.SendAsync(Drag(ContainerId.Inventory, 0, ContainerId.Inventory, 1));
        await _test.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Logs, 1), Inventory("ana").At(0));
        Assert.Equal(new ItemStack(ItemId.Coins, 5), Inventory("ana").At(1));
        Assert.Equal(2, ana.ItemDeltas[^1].Changes.Count);
    }

    [Fact]
    public async Task WearsAHelmetDraggedToTheHeadSlot()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.BronzeHelmet, 1);
        await _test.TickAsync();
        var head = ItemDef.EquipSlots.ToList().IndexOf("head");

        await ana.SendAsync(Drag(ContainerId.Inventory, 0, ContainerId.Equipment, head));
        await _test.TickAsync();

        Assert.Equal(new ItemStack(ItemId.BronzeHelmet, 1), Of("ana", ContainerId.Equipment).At(head));
        Assert.Null(Inventory("ana").At(0));
    }

    [Fact]
    public async Task ARefusedDragChangesNothingAndSaysWhy()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 5);
        await _test.TickAsync();
        var before = ana.ItemDeltas.Count;

        await ana.SendAsync(Drag(ContainerId.Inventory, 0, ContainerId.Equipment, 0));
        await _test.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Coins, 5), Inventory("ana").At(0));
        Assert.Equal(before, ana.ItemDeltas.Count);
        Assert.Equal("That does not go there.", ana.Notices[^1]);
    }

    [Fact]
    public async Task ASlotThatHoldsNothingIsRefused()
    {
        var ana = await _test.JoinAsync("Ana");

        await ana.SendAsync(Drag(ContainerId.Inventory, 0, ContainerId.Inventory, 1));
        await _test.TickAsync();

        Assert.Equal("There is nothing to move there.", ana.Notices[^1]);
    }

    // Ground items.

    [Fact]
    public async Task AnItemDroppedIsSeenByItsOwnerAndNotByOthers()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");

        var id = await DropFromAnaAsync(ana);

        Assert.Equal((ItemId.Coins, 10), Assert.Single(ana.GroundItems, pair => pair.Key == id).Value);
        Assert.Single(ana.GroundItems);
        Assert.Empty(bo.GroundItems);
        Assert.Equal(Spawn, ana.TileOf(id));
    }

    [Fact]
    public async Task DroppingTakesTheItemFromTheInventory()
    {
        var ana = await _test.JoinAsync("Ana");

        await DropFromAnaAsync(ana, count: 4);

        Assert.Equal(new ItemStack(ItemId.Coins, 6), Inventory("ana").At(0));
        Assert.Equal((ItemId.Coins, 4), Assert.Single(ana.GroundItems.Values));
    }

    [Fact]
    public async Task IsHiddenFromOthersUntilTheOwnerTimerEnds()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        var id = await DropFromAnaAsync(ana);

        await _test.TickAsync(GroundItems.OwnerTicks - 2);
        Assert.Empty(bo.GroundItems);

        await _test.TickAsync();
        Assert.Equal(new Dictionary<long, (int, int)> { [id] = (ItemId.Coins, 10) }, bo.GroundItems);
        Assert.Equal(new Dictionary<long, (int, int)> { [id] = (ItemId.Coins, 10) }, ana.GroundItems);
    }

    [Fact]
    public async Task VanishesForEveryoneAfterThePublicTime()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await DropFromAnaAsync(ana);
        await _test.TickAsync(GroundItems.OwnerTicks - 1);
        Assert.Single(bo.GroundItems);

        await _test.TickAsync(GroundItems.PublicTicks - 1);
        Assert.Single(ana.GroundItems);
        Assert.Single(bo.GroundItems);

        await _test.TickAsync();
        Assert.Empty(ana.GroundItems);
        Assert.Empty(bo.GroundItems);
        Assert.Empty(Server.Ground.LyingAt("ana", Spawn));
    }

    [Fact]
    public async Task AnItemWithNoOwnerIsOpenToAllAtOnce()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");

        var id = (long)Server.Ground.Drop(ItemId.Logs, 1, Spawn);
        await _test.TickAsync();

        Assert.Equal(new Dictionary<long, (int, int)> { [id] = (ItemId.Logs, 1) }, ana.GroundItems);
        Assert.Equal(new Dictionary<long, (int, int)> { [id] = (ItemId.Logs, 1) }, bo.GroundItems);
    }

    [Fact]
    public async Task AnItemWithNoOwnerLiesForThePublicTimeOnly()
    {
        var ana = await _test.JoinAsync("Ana");
        Server.Ground.Drop(ItemId.Logs, 1, Spawn);
        await _test.TickAsync();

        await _test.TickAsync(GroundItems.PublicTicks - 1);

        Assert.Empty(ana.GroundItems);
    }

    [Fact]
    public async Task TheOwnerLeavingDoesNotEndTheClaim()
    {
        var ana = await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");
        await DropFromAnaAsync(ana);
        await _test.LeaveAsync(ana);

        await _test.TickAsync(GroundItems.OwnerTicks - 3);

        Assert.Empty(bo.GroundItems);
    }

    [Fact]
    public async Task LyingAtListsWhatAPlayerCanSeeOnATile()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        var id = await DropFromAnaAsync(ana);

        Assert.Equal([new Lying((ulong)id, ItemId.Coins, 10)], Server.Ground.LyingAt("ana", Spawn));
        Assert.Empty(Server.Ground.LyingAt("bo", Spawn));
        Assert.Empty(Server.Ground.LyingAt("ana", new TileCoord(1, 1, 0)));
    }

    [Fact]
    public void RefusesAnItemThatDoesNotExistAndACountOfNone()
    {
        Assert.ThrowsAny<ArgumentException>(() => Server.Ground.Drop(9999, 1, Spawn));
        Assert.ThrowsAny<ArgumentException>(() => Server.Ground.Drop(ItemId.Logs, 0, Spawn));
    }

    [Fact]
    public async Task DroppingFromAnEmptySlotOrAPlayerNotHereIsNothing()
    {
        await _test.JoinAsync("Ana");

        Assert.Equal(Outcome.NothingThere, Server.Ground.DropFrom("ana", 0));
        Assert.Equal(Outcome.NothingThere, Server.Ground.DropFrom("ana", 99));
        Assert.Equal(Outcome.NothingThere, Server.Ground.DropFrom("nobody", 0));
    }

    [Fact]
    public async Task AStackAtTheIntegerLimitCanBeDroppedWhole()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, ItemStack.MaxCount);
        await _test.TickAsync();

        Assert.Equal(Outcome.Done, Server.Ground.DropFrom("ana", 0));
        await _test.TickAsync();

        Assert.Equal((ItemId.Coins, ItemStack.MaxCount), Assert.Single(ana.GroundItems.Values));
    }

    // Picking up.

    [Fact]
    public async Task TheOwnerOnTheTileTakesTheItemIntoTheInventory()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Coins, 10);
        await _test.TickAsync();
        Server.Ground.DropFrom("ana", 0);
        await _test.TickAsync();
        var id = Assert.Single(ana.GroundItems.Keys);

        Assert.Equal(Outcome.Done, Server.Ground.PickUp("ana", (ulong)id));
        await _test.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Coins, 10), Inventory("ana").At(0));
        Assert.Empty(ana.GroundItems);
        Assert.Empty(Server.Ground.LyingAt("ana", Spawn));
    }

    [Fact]
    public async Task AnotherPlayerCannotTakeAnItemTheyCannotSee()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        var id = Server.Ground.Drop(ItemId.Logs, 1, Spawn, owner: "ana");
        await _test.TickAsync();

        Assert.Equal(Outcome.NothingThere, Server.Ground.PickUp("bo", id));
        Assert.Null(Inventory("bo").At(0));
        Assert.Equal([(long)id], ana.GroundItems.Keys);
    }

    [Fact]
    public async Task AnotherPlayerMayTakeItOnceItIsPublic()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        var id = Server.Ground.Drop(ItemId.Logs, 1, Spawn, owner: "ana");
        await _test.TickAsync(GroundItems.OwnerTicks);

        Assert.Equal(Outcome.Done, Server.Ground.PickUp("bo", id));
        await _test.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Logs, 1), Inventory("bo").At(0));
        Assert.Empty(ana.GroundItems);
    }

    [Fact]
    public async Task APlayerNotOnTheTileCannotReachIt()
    {
        await _test.JoinAsync("Ana");
        var far = new TileCoord(40, 40, 0);
        var id = Server.Ground.Drop(ItemId.Logs, 1, far);

        Assert.Equal(Outcome.NotAllowed, Server.Ground.PickUp("ana", id));
        Assert.Single(Server.Ground.LyingAt("ana", far));
    }

    [Fact]
    public async Task AnItemThatIsGoneAndAPlayerWhoIsNotHereGiveNothing()
    {
        await _test.JoinAsync("Ana");
        var id = Server.Ground.Drop(ItemId.Logs, 1, Spawn);

        Assert.Equal(Outcome.NothingThere, Server.Ground.PickUp("ana", id + 100));
        Assert.Equal(Outcome.NothingThere, Server.Ground.PickUp("nobody", id));
    }

    [Fact]
    public async Task AFullInventoryTakesNothingAndTheItemStays()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize);
        var id = Server.Ground.Drop(ItemId.Logs, 1, Spawn);

        Assert.Equal(Outcome.WontFit, Server.Ground.PickUp("ana", id));
        await _test.TickAsync();

        Assert.Equal([(long)id], ana.GroundItems.Keys);
    }

    [Fact]
    public async Task RoomForPartTakesThatPartAndUpdatesWhatIsLeft()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize - 1);
        Inventory("ana").Add(ItemId.Coins, ItemStack.MaxCount - 3);
        var id = Server.Ground.Drop(ItemId.Coins, 10, Spawn);
        await _test.TickAsync();

        Assert.Equal(Outcome.Partly, Server.Ground.PickUp("ana", id));
        await _test.TickAsync();

        Assert.Equal(ItemStack.MaxCount, Inventory("ana").CountOf(ItemId.Coins));
        Assert.Equal(new Dictionary<long, (int, int)> { [(long)id] = (ItemId.Coins, 7) }, ana.GroundItems);
    }

    private async Task<long> DropFromAnaAsync(TestClient ana, int? count = null)
    {
        Inventory("ana").Add(ItemId.Coins, 10);
        await _test.TickAsync();
        Assert.Equal(Outcome.Done, Server.Ground.DropFrom("ana", 0, count));
        await _test.TickAsync();
        return Assert.Single(ana.GroundItems.Keys);
    }
}

/// <summary>What a client was told about its containers, read the way the item tests ask.</summary>
file static class ItemReading
{
    extension(TestClient client)
    {
        /// <summary>The container changes this player was sent, oldest first.</summary>
        public IReadOnlyList<SlotsDelta> ItemDeltas =>
        [
            .. client.Messages.OfType<PrivateState>()
                .Where(state => state.Delta.Span[0] == ItemSet.DeltaTag)
                .Select(state => ContainerCodec.TryDecodeSlots(state.Delta.Span, DefaultItems.All)!.Value),
        ];
    }
}
