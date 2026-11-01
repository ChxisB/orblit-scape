using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class GatheringTests : IAsyncDisposable
{
    /// <summary>A try that succeeds, then a node that stays full.</summary>
    private static readonly double[] Gives = [0.0, 0.99];

    /// <summary>A try that succeeds and then runs the node dry.</summary>
    private static readonly double[] GivesAndDries = [0.0, 0.0];

    /// <summary>A try that finds nothing.</summary>
    private static readonly double[] Misses = [0.99];

    private const int Ticks = Gathering.Ticks;

    private static readonly XpCurve Curve = DefaultSkills.All.Curve;

    private TestServer _test;
    private Rolls _rolls;
    private PlacedObject _tree;

    /// <summary>Every test starts on dice that give, with the default nodes. <see cref="StartAsync"/> swaps them.</summary>
    public GatheringTests() => (_test, _rolls, _tree) = Build(Gives, null);

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static (TestServer, Rolls, PlacedObject) Build(double[] dice, NodeSet? nodes)
    {
        var rolls = new Rolls(dice);
        var test = new TestServer(options => options with { Random = rolls, Nodes = nodes ?? options.Nodes });
        return (test, rolls, test.Server.Objects.Place(ObjectId.Tree, At(33, 32)));
    }

    private async Task StartAsync(double[] dice, NodeSet? nodes = null)
    {
        await _test.DisposeAsync();
        (_test, _rolls, _tree) = Build(dice, nodes);
    }

    private Container Inventory(string key) => Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    private static Task ClickAsync(TestClient client, PlacedObject placed) =>
        client.ClickAsync(InteractKind.PlacedObject, (long)placed.NetworkId);

    /// <summary>Joins a player holding an axe and starts them chopping.</summary>
    private async Task<TestClient> ChopperAsync(string name = "Ana")
    {
        var player = await _test.JoinAsync(name);
        Inventory(name.ToLowerInvariant()).Add(ItemId.BronzeAxe, 1);
        await ClickAsync(player, _tree);
        return player;
    }

    private static NodeDef TreeNode(int level = 1, int respawnTicks = 25) =>
        new(ObjectId.Tree, "woodcutting", level, ItemId.BronzeAxe, 25, ItemId.Logs, 0.3, 0.7, 30, 0.15, respawnTicks);

    private static NodeSet Only(NodeDef node) => new([node], DefaultObjects.All, DefaultItems.All, DefaultSkills.All);

    // A try.

    [Fact]
    public async Task ATryThatRollsUnderTheChanceGivesTheProductAndXp()
    {
        await ChopperAsync();

        await _test.TickAsync(Ticks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Equal(25, Server.Skills.XpOf("ana", "woodcutting"));
    }

    [Fact]
    public async Task ATryWaitsTheGatherDelayBeforeEachTry()
    {
        await ChopperAsync();

        await _test.TickAsync(Ticks);
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Logs));

        await _test.TickAsync();
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));

        await _test.TickAsync(Ticks);
        Assert.Equal(2, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ATryThatRollsOverTheChanceGivesNothingAndTriesAgain()
    {
        await StartAsync([.. Misses, .. Misses, .. Gives]);
        await ChopperAsync();

        await _test.TickAsync(Ticks * 2 + 1);
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Equal(0, Server.Skills.XpOf("ana", "woodcutting"));

        await _test.TickAsync(Ticks);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ATryIsJudgedAgainstTheChanceAtThePlayerLevel()
    {
        await StartAsync([0.5]);
        var ana = await ChopperAsync();
        await _test.TickAsync(Ticks + 1);
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Logs));

        Server.Skills.Grant("ana", "woodcutting", Curve.XpForLevel(30));
        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ATryAtTheChanceItselfDoesNotSucceed()
    {
        await StartAsync([0.3]);
        await ChopperAsync();

        await _test.TickAsync(Ticks + 1);

        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Logs));
    }

    // Experience.

    [Fact]
    public async Task ALevelGainedIsToldOnce()
    {
        var ana = await ChopperAsync();
        Server.Skills.Grant("ana", "woodcutting", Curve.XpForLevel(2) - 10);

        await _test.TickAsync(Ticks + 1);

        Assert.Equal(["You reached level 2 in woodcutting."], ana.Notices);
    }

    // A refusal.

    [Fact]
    public async Task ARefusalWithoutTheToolSaysWhichTool()
    {
        var ana = await _test.JoinAsync("Ana");

        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks * 2);

        Assert.Equal(["You need a bronze axe to do that."], ana.Notices);
        Assert.Equal(0, _rolls.Drawn);
    }

    [Fact]
    public async Task TheToolWornIsNotRefused()
    {
        var ana = await _test.JoinAsync("Ana");
        Server.Items.ContainerOf("ana", ContainerId.Equipment)!.Add(ItemId.BronzeAxe, 1);

        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks + 1);

        Assert.Empty(ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ARefusalOneLevelShortSaysWhichLevel()
    {
        await StartAsync(Gives, Only(TreeNode(level: 5)));
        var ana = await ChopperAsync();
        Server.Skills.Grant("ana", "woodcutting", Curve.XpForLevel(4));

        await _test.TickAsync(Ticks * 2);

        Assert.Equal(["You need level 5 in woodcutting to do that."], ana.Notices);
        Assert.Equal(0, _rolls.Drawn);
    }

    [Fact]
    public async Task AtTheLevelNeededItGathers()
    {
        await StartAsync(Gives, Only(TreeNode(level: 5)));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.BronzeAxe, 1);
        Server.Skills.Grant("ana", "woodcutting", Curve.XpForLevel(5));

        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks + 1);

        Assert.Empty(ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ARefusalWithAFullBagIsToldAndGivesNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.BronzeAxe, 1);
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize - 1);

        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks * 2);

        Assert.Equal(["Your inventory is full."], ana.Notices);
        Assert.Equal(Container.InventorySize - 1, Inventory("ana").CountOf(ItemId.Logs));
    }

    // The loop.

    [Fact]
    public async Task TheLoopStopsWhenTheBagFills()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.BronzeAxe, 1);
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize - 2);

        await ClickAsync(ana, _tree);
        await _test.TickAsync(Ticks * 8);

        Assert.Equal(Container.InventorySize - 1, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Equal(["Your inventory is full."], ana.Notices);
    }

    [Fact]
    public async Task TheLoopStopsWhenThePlayerWalksAway()
    {
        var ana = await ChopperAsync();
        await _test.TickAsync(Ticks + 1);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));

        await ana.SendAsync(new Walk(At(40, 40)));
        await _test.TickAsync(Ticks * 4);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task TheLoopStopsWhenThePlayerLeaves()
    {
        await ChopperAsync();
        await _test.TickAsync(Ticks + 1);
        var before = _rolls.Drawn;

        Server.Host.RemoveClient("ana");
        await _test.TickAsync(Ticks * 3);

        Assert.Equal(before, _rolls.Drawn);
    }

    // A node that runs dry.

    [Fact]
    public async Task ANodeThatRunsDryTurnsBareAndStopsTheLoop()
    {
        await StartAsync(GivesAndDries);
        var ana = await ChopperAsync();

        await _test.TickAsync(Ticks * 3);

        Assert.Equal(NodeState.Depleted, _tree.State);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Empty(ana.Notices);
    }

    [Fact]
    public async Task ASecondPlayerWhoFindsItDryIsTold()
    {
        await StartAsync(GivesAndDries);
        await ChopperAsync();
        var bob = await ChopperAsync("Bob");

        await _test.TickAsync(Ticks * 3);

        Assert.Equal(["There is nothing left."], bob.Notices);
        Assert.Equal(0, Inventory("bob").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ADryNodeFillsAgainOnItsRespawnTick()
    {
        await StartAsync(GivesAndDries);
        await ChopperAsync();
        await TickUntilDryAsync();

        await _test.TickAsync(TreeNode().RespawnTicks - 1);
        Assert.Equal(NodeState.Depleted, _tree.State);

        await _test.TickAsync();
        Assert.Equal(NodeState.Full, _tree.State);
    }

    [Fact]
    public async Task ANodeDriedByHandAfterItFilledIsNotRefilled()
    {
        await StartAsync(GivesAndDries);
        await ChopperAsync();
        await TickUntilDryAsync();
        await _test.TickAsync(TreeNode().RespawnTicks);
        Assert.Equal(NodeState.Full, _tree.State);

        Server.Objects.SetState(_tree, NodeState.Depleted);
        await _test.TickAsync(3);

        Assert.Equal(NodeState.Depleted, _tree.State);
    }

    [Fact]
    public async Task ADryNodeWaitsForAPlayerToBeNearBeforeItFills()
    {
        await StartAsync(GivesAndDries, Only(TreeNode(respawnTicks: 80)));
        var ana = await ChopperAsync();
        await _test.TickAsync(Ticks + 1);
        Assert.Equal(NodeState.Depleted, _tree.State);

        await ana.SendAsync(new Walk(At(100, 32)));
        await _test.TickAsync(120);
        Assert.False(Server.Active.IsActiveAt(33, 32));
        Assert.Equal(NodeState.Depleted, _tree.State);

        await ana.SendAsync(new Walk(At(36, 32)));
        var ticks = 0;
        while (!Server.Active.IsActiveAt(33, 32))
        {
            await _test.TickAsync();
            Assert.True(++ticks < 120);
        }

        Assert.Equal(NodeState.Full, _tree.State);
    }

    // Other nodes.

    [Theory]
    [InlineData(ObjectId.Rock, ItemId.BronzePickaxe, ItemId.CopperOre, "mining")]
    [InlineData(ObjectId.FishingSpot, ItemId.FishingNet, ItemId.RawFish, "fishing")]
    public async Task ATryAtAnotherNodeGivesItsOwnProductAndXp(int objectId, int tool, int product, string skill)
    {
        var node = Server.Objects.Place(objectId, At(32, 33));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(tool, 1);

        await ClickAsync(ana, node);
        await _test.TickAsync(Ticks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(product));
        Assert.True(Server.Skills.XpOf("ana", skill) > 0);
    }

    private async Task TickUntilDryAsync()
    {
        var ticks = 0;
        while (_tree.State == NodeState.Full)
        {
            await _test.TickAsync();
            Assert.True(++ticks < 20);
        }
    }
}
