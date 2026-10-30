using Scape.Content;
using Scape.Core;

namespace Scape.Client.Tests;

public sealed class InterfaceSessionTests : IAsyncDisposable
{
    private readonly InProcessGame _game = new();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    private static Menus MenusOf(GameSession session) =>
        new(session, DefaultNpcs.All, DefaultObjects.All, DefaultItems.All);

    private Container Bag(string key) => _game.Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    private long Spawn(int npc, TileCoord tile) => (long)_game.Server.Npcs.Spawn(new NpcSpawn(npc, tile)).NetworkId;

    [Fact]
    public async Task AMovedItemShowsInThePanelOnceTheServerConfirms()
    {
        var ana = await _game.JoinAsync("Ana");
        Bag("ana").Add(ItemId.Coins, 5);
        Bag("ana").Add(ItemId.Logs, 1);
        await _game.TickAsync();

        ana.MoveItem(ContainerId.Inventory, 0, ContainerId.Inventory, 1);
        Assert.Equal(new ItemStack(ItemId.Coins, 5), ana.ItemAt(ContainerId.Inventory, 0));
        await _game.PumpAsync();
        await _game.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Equal(new ItemStack(ItemId.Coins, 5), ana.ItemAt(ContainerId.Inventory, 1));
    }

    [Fact]
    public async Task ARefusedMoveLeavesThePanelAsItWas()
    {
        var ana = await _game.JoinAsync("Ana");
        Bag("ana").Add(ItemId.Coins, 5);
        await _game.TickAsync();

        ana.MoveItem(ContainerId.Inventory, 0, ContainerId.Equipment, ItemDef.EquipSlots.ToList().IndexOf("head"));
        await _game.PumpAsync();
        await _game.TickAsync();

        Assert.Equal(new ItemStack(ItemId.Coins, 5), ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Null(ana.ItemAt(ContainerId.Equipment, 0));
        Assert.Equal("That does not go there.", ana.Notices[^1]);
    }

    [Fact]
    public async Task TheJournalIsEmptyUntilAQuestBeginsThenFollowsTheStage()
    {
        var ana = _game.Connect();
        ana.Login("Ana");
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana);
        Assert.Empty(ana.Journal);
        var brindle = Spawn(NpcId.Lamplighter, new TileCoord(33, 32, 0));
        await _game.TickAndDrawAsync(ana, 2);

        ana.Talk(brindle);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 6);
        ana.ContinueTalk();
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 2);
        ana.Choose(0);
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 4);

        var entry = Assert.Single(ana.Journal);
        Assert.Equal("The Dark Lantern", entry.Title);
        Assert.Contains("Ask the Guard", entry.Text);
        Assert.False(entry.Done);
    }

    [Fact]
    public async Task TheChatBoxKeepsGamePublicAndPrivateLinesApart()
    {
        var ana = await _game.JoinAsync("Ana");
        var bo = await _game.JoinAsync("Bo");
        ana.Say("hello");
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(bo);
        ana.Whisper("Bo", "psst");
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(bo);

        Assert.Equal(["You: hello"], ana.ChatOf(ChatTab.Public));
        Assert.Equal(["Someone: hello"], bo.ChatOf(ChatTab.Public));
        Assert.Equal(["ana: psst"], bo.ChatOf(ChatTab.Private));
        Assert.Empty(bo.ChatOf(ChatTab.Game));
    }

    [Fact]
    public async Task ACreatureOffersWhatItsContentGivesIt()
    {
        var ana = await _game.JoinAsync("Ana");
        var goblin = Spawn(NpcId.Goblin, new TileCoord(40, 32, 0));
        var shopkeeper = Spawn(NpcId.Shopkeeper, new TileCoord(34, 32, 0));
        await _game.TickAndDrawAsync(ana, 2);

        var labels = ana.Npcs.ToDictionary(sprite => sprite.NetworkId, sprite => MenusOf(ana).OnNpc(sprite).Select(entry => entry.Label).ToList());

        Assert.Equal(["Attack Goblin"], labels[goblin]);
        Assert.Equal(["Trade Shopkeeper", "Talk to Shopkeeper"], labels[shopkeeper]);
    }

    [Fact]
    public async Task AnObjectOffersTheOptionForTheStateItIsIn()
    {
        var ana = await _game.JoinAsync("Ana");
        var door = _game.Server.Objects.Place(ObjectId.Door, new TileCoord(34, 32, 0));
        await _game.TickAndDrawAsync(ana, 2);
        Assert.Equal(["Open Door"], MenusOf(ana).OnObject(Assert.Single(ana.Objects)).Select(entry => entry.Label));

        _game.Server.Objects.SetState(door, 1);
        await _game.TickAndDrawAsync(ana, 2);

        Assert.Equal(["Close Door"], MenusOf(ana).OnObject(Assert.Single(ana.Objects)).Select(entry => entry.Label));
    }

    [Fact]
    public async Task AnEntrySendsTheClickItNames()
    {
        var ana = await _game.JoinAsync("Ana");
        var id = (long)_game.Server.Ground.Drop(ItemId.Logs, 1, new TileCoord(33, 32, 0));
        await _game.TickAndDrawAsync(ana, 2);
        var take = Assert.Single(MenusOf(ana).OnGround(Assert.Single(ana.GroundItems)));

        take.Run();
        await _game.PumpAsync();
        await _game.TickAndDrawAsync(ana, 3);

        Assert.Equal("Take Logs", take.Label);
        Assert.DoesNotContain(ana.GroundItems, ground => ground.NetworkId == id);
        Assert.Equal(new ItemStack(ItemId.Logs, 1), ana.ItemAt(ContainerId.Inventory, 0));
    }

    [Fact]
    public async Task AnotherPlayerOffersATradeYouDoNot()
    {
        var ana = await _game.JoinAsync("Ana");
        await _game.JoinAsync("Bo");
        await _game.TickAndDrawAsync(ana, 2);

        var entries = ana.Actors.ToDictionary(actor => actor.Mine, actor => MenusOf(ana).OnPlayer(actor).Count);

        Assert.Equal(new Dictionary<bool, int> { [true] = 0, [false] = 1 }, entries);
    }

    [Fact]
    public async Task TheMinimapShowsOthersNearThePlayerAndNotThePlayer()
    {
        var ana = await _game.JoinAsync("Ana");
        Spawn(NpcId.Guard, new TileCoord(35, 34, 0));
        Spawn(NpcId.Guard, new TileCoord(60, 60, 0));
        await _game.TickAndDrawAsync(ana, 2);

        var dot = Assert.Single(Minimap.Dots(ana));

        Assert.Equal(DotKind.Npc, dot.Kind);
        Assert.Equal((3.0, 2.0), (dot.Dx, dot.Dy));
    }

    [Fact]
    public async Task TheMinimapLeavesOutACreatureOnAnotherPlane()
    {
        var ana = await _game.JoinAsync("Ana");
        Spawn(NpcId.Guard, new TileCoord(35, 34, 1));
        await _game.TickAndDrawAsync(ana, 2);

        Assert.Single(ana.Npcs);
        Assert.Empty(Minimap.Dots(ana));
    }

    [Fact]
    public void TheMinimapIsEmptyBeforeThePlayerIsInTheWorld()
    {
        Assert.Empty(Minimap.Dots(_game.Connect()));
    }
}
