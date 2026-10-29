using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class BankTests : IAsyncDisposable
{
    private readonly TestServer _test = new();
    private readonly PlacedObject _booth;

    public BankTests() => _booth = Server.Objects.Place(ObjectId.BankBooth, new TileCoord(33, 32, 0));

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private Container Inventory => Server.Items.ContainerOf("ana", ContainerId.Inventory)!;

    private Container Bank => Server.Items.ContainerOf("ana", ContainerId.Bank)!;

    /// <summary>Joins Ana and, when <paramref name="open"/> is set, has her open the booth.</summary>
    private async Task<TestClient> AnaAsync(bool open = true)
    {
        var client = _test.Connect();
        await client.LoginAsync("Ana");
        await _test.TickAsync(2);
        if (open)
        {
            await client.ClickAsync(InteractKind.PlacedObject, (long)_booth.NetworkId);
            await _test.TickAsync(4);
        }

        return client;
    }

    [Fact]
    public async Task UsingTheBoothOpensTheBank()
    {
        var ana = await AnaAsync();

        Assert.Single(ana.Messages.OfType<BankOpen>());
    }

    [Fact]
    public async Task ADepositMovesPartOfAStackIntoTheChosenTab()
    {
        var ana = await AnaAsync();
        Inventory.Add(ItemId.Feathers, 10);

        await ana.SendAsync(new Deposit(0, 4, 3));
        await _test.TickAsync(2);

        Assert.Equal(6, Inventory.CountOf(ItemId.Feathers));
        Assert.Equal(4, Bank.CountOf(ItemId.Feathers));
        Assert.Single(Bank.Occupied(tab: 3));
        Assert.Empty(Bank.Occupied(tab: 0));
    }

    [Fact]
    public async Task AWithdrawalMovesAStackBack()
    {
        var ana = await AnaAsync();
        Bank.Add(ItemId.Feathers, 3, tab: 0);

        await ana.SendAsync(new Withdraw(0, 2));
        await _test.TickAsync(2);

        Assert.Equal(2, Inventory.CountOf(ItemId.Feathers));
        Assert.Equal(1, Bank.CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task AWithdrawalWithNoRoomChangesNothing()
    {
        var ana = await AnaAsync();
        Bank.Add(ItemId.CopperOre, 3, tab: 0);
        while (Inventory.Occupied().Count < Container.InventorySize)
        {
            Inventory.Add(ItemId.Bones, 1);
        }

        await ana.SendAsync(new Withdraw(0, 3));
        await _test.TickAsync(2);

        Assert.Contains("There is no room for that.", ana.Notices);
        Assert.Equal(3, Bank.CountOf(ItemId.CopperOre));
    }

    [Fact]
    public async Task AnEmptySlotIsRefused()
    {
        var ana = await AnaAsync();

        await ana.SendAsync(new Withdraw(5, 1));
        await _test.TickAsync(2);

        Assert.Contains("There is nothing in that slot.", ana.Notices);
    }

    [Fact]
    public async Task ATabThatDoesNotExistIsRefused()
    {
        var ana = await AnaAsync();
        Inventory.Add(ItemId.Feathers, 1);

        await ana.SendAsync(new Deposit(0, 1, Container.BankTabs));
        await _test.TickAsync(2);

        Assert.Equal(1, Inventory.CountOf(ItemId.Feathers));
        Assert.Equal(0, Bank.CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task NothingMovesWhileTheBankIsClosed()
    {
        var ana = await AnaAsync(open: false);
        Inventory.Add(ItemId.Feathers, 5);
        Bank.Add(ItemId.CopperOre, 1, tab: 0);

        await ana.SendAsync(new Deposit(0, 5, 0));
        await ana.SendAsync(new Withdraw(0, 1));
        await _test.TickAsync(2);

        Assert.Equal(5, Inventory.CountOf(ItemId.Feathers));
        Assert.Equal(1, Bank.CountOf(ItemId.CopperOre));
    }

    [Fact]
    public async Task NothingMovesWhileAShopIsOpen()
    {
        var ana = await AnaAsync(open: false);
        var keeper = Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, new TileCoord(31, 32, 0)));
        await ana.ClickAsync(InteractKind.Npc, (long)keeper.NetworkId, NpcOption.Trade);
        await _test.TickAsync(4);
        Inventory.Add(ItemId.Feathers, 5);

        await ana.SendAsync(new Deposit(0, 5, 0));
        await _test.TickAsync(2);

        Assert.NotEmpty(ana.Messages.OfType<ShopView>());
        Assert.Equal(5, Inventory.CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task WalkingAwayClosesItAndEndsTheUseOfIt()
    {
        var ana = await AnaAsync();
        Inventory.Add(ItemId.Feathers, 5);

        await ana.SendAsync(new Walk(new TileCoord(60, 60, 0)));
        await _test.TickAsync(12);
        await ana.SendAsync(new Deposit(0, 5, 0));
        await _test.TickAsync(2);

        Assert.Single(ana.Messages.OfType<WindowClosed>());
        Assert.Equal(5, Inventory.CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task TheBankIsSearchableByName()
    {
        await AnaAsync();
        Bank.Add(ItemId.CopperOre, 1, tab: 0);
        Bank.Add(ItemId.Logs, 1, tab: 2);

        Assert.Single(Bank.Search("ore"));
        Assert.Single(Bank.Search("LOGS", tab: 2));
        Assert.Empty(Bank.Search("logs", tab: 0));
    }
}
