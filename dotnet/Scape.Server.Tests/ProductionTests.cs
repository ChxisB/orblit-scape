using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class ProductionTests : IAsyncDisposable
{
    /// <summary>A make that works at any level the recipes set.</summary>
    private static readonly double[] Works = [0.99];

    /// <summary>A make that fails whenever it can.</summary>
    private static readonly double[] Fails = [0.0];

    private const int MakeTicks = 3;

    private TestServer _test;
    private Rolls _rolls;
    private PlacedObject _fire;

    /// <summary>Every test starts on dice that work, with the default recipes. <see cref="StartAsync"/> swaps them.</summary>
    public ProductionTests() => (_test, _rolls, _fire) = Build(Works, null);

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static (TestServer, Rolls, PlacedObject) Build(double[] dice, RecipeSet? recipes)
    {
        var rolls = new Rolls(dice);
        var test = new TestServer(options => options with { Random = rolls, Recipes = recipes ?? DefaultRecipes.All });
        return (test, rolls, test.Server.Objects.Place(ObjectId.Fire, At(33, 32)));
    }

    private async Task StartAsync(double[] dice, RecipeSet? recipes = null)
    {
        await _test.DisposeAsync();
        (_test, _rolls, _fire) = Build(dice, recipes);
    }

    private Container Inventory(string key) => Server.Items.ContainerOf(key, ContainerId.Inventory)!;

    private void Level(string player, string skill, int level) =>
        Server.Skills.Grant(player, skill, DefaultSkills.All.Curve.XpForLevel(level));

    private static Task UseOnAsync(TestClient client, int slot, PlacedObject placed) =>
        client.ClickAsync(InteractKind.UseOnObject, (long)placed.NetworkId, slot);

    private static Task UseOnItemAsync(TestClient client, int slot, int onSlot) =>
        client.ClickAsync(InteractKind.UseOnItem, onSlot, slot);

    private static RecipeSet Only(Recipe recipe) =>
        new([recipe], DefaultItems.All, DefaultObjects.All, DefaultSkills.All);

    private static Recipe Cook(int level = 1, int inputs = 1) => new(
        "cook the fish",
        "cooking",
        level,
        30,
        new OnObject(ItemId.RawFish, ObjectId.Fire),
        [new ItemStack(ItemId.RawFish, inputs)],
        new Gives(new ItemStack(ItemId.CookedFish, 1)),
        failure: new Failure(0.5, level + 29, ItemId.BurntFish));

    // Cooking.

    [Fact]
    public async Task CookingUsesTheFishAndGivesACookedOneAfterTheDelay()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks);
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.CookedFish));

        await _test.TickAsync();
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.RawFish));
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(30, Server.Skills.XpOf("ana", "cooking"));
    }

    [Fact]
    public async Task CookingKeepsGoingUntilTheFishRunOutAndThenStops()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 3);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 8);

        Assert.Equal(3, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(3, _rolls.Drawn);
        Assert.Equal(["You reached level 2 in cooking."], ana.Notices);
    }

    [Fact]
    public async Task CookingBurnsTheFishForARollUnderTheFailureChance()
    {
        await StartAsync(Fails);
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal(0, Inventory("ana").CountOf(ItemId.RawFish));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BurntFish));
        Assert.Equal(0, Server.Skills.XpOf("ana", "cooking"));
        Assert.Equal(["You fail to cook the fish."], ana.Notices);
    }

    [Fact]
    public async Task CookingKeepsGoingAfterABurntFish()
    {
        await StartAsync([0.0, 0.99, 0.99]);
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 3);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 8);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BurntFish));
        Assert.Equal(2, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.RawFish));
    }

    [Fact]
    public async Task CookingNeverBurnsAtTheSafeLevel()
    {
        await StartAsync(Fails);
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "cooking", 30);
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BurntFish));
    }

    [Fact]
    public async Task CookingStopsWhenTheFireGoesOut()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 3);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);
        Server.Objects.Remove(_fire);
        await _test.TickAsync(MakeTicks * 4);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
        Assert.Equal(2, Inventory("ana").CountOf(ItemId.RawFish));
        Assert.Equal("The fire has gone.", ana.Notices[^1]);
    }

    [Fact]
    public async Task CookingStopsWhenThePlayerWalksAway()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 3);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);
        await ana.SendAsync(new Walk(At(40, 40)));
        await _test.TickAsync(MakeTicks * 4);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
    }

    [Fact]
    public async Task CookingStopsWhenThePlayerLeaves()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 3);
        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);
        var before = _rolls.Drawn;

        Server.Host.RemoveClient("ana");
        await _test.TickAsync(MakeTicks * 3);

        Assert.Equal(before, _rolls.Drawn);
    }

    // A refusal.

    [Fact]
    public async Task ARefusalOneLevelShortSaysWhichLevel()
    {
        await StartAsync(Works, Only(Cook(level: 5)));
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "cooking", 4);
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 2);

        Assert.Equal(["You need level 5 in cooking to do that."], ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.RawFish));
        Assert.Equal(0, _rolls.Drawn);
    }

    [Fact]
    public async Task AtTheLevelNeededItMakes()
    {
        await StartAsync(Works, Only(Cook(level: 5)));
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "cooking", 5);
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Empty(ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
    }

    [Fact]
    public async Task ARefusalWithoutTheToolSaysWhichTool()
    {
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "smithing", 4);
        Inventory("ana").Add(ItemId.BronzeBar, 1);
        var anvil = Server.Objects.Place(ObjectId.Anvil, At(33, 33));

        await UseOnAsync(ana, 0, anvil);
        await _test.TickAsync(MakeTicks * 2);

        Assert.Equal(["You need a hammer to do that."], ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BronzeBar));
    }

    [Fact]
    public async Task AToolThatIsWornCountsAsHeld()
    {
        var recipe = new Recipe(
            "cook the fish",
            "cooking",
            1,
            30,
            new OnObject(ItemId.RawFish, ObjectId.Fire),
            [new ItemStack(ItemId.RawFish, 1)],
            new Gives(new ItemStack(ItemId.CookedFish, 1)),
            tool: ItemId.BronzeAxe);
        await StartAsync(Works, Only(recipe));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 1);
        Server.Items.ContainerOf("ana", ContainerId.Equipment)!.Add(ItemId.BronzeAxe, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
    }

    [Fact]
    public async Task ARefusalWithTooFewInputsChangesNothing()
    {
        await StartAsync(Works, Only(Cook(inputs: 2)));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 2);

        Assert.Equal(["You do not have what you need."], ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.RawFish));
        Assert.Equal(0, _rolls.Drawn);
    }

    [Fact]
    public async Task ARefusalWithNoRoomForTheProductChangesNothing()
    {
        var recipe = new Recipe(
            "cook the feathers",
            "cooking",
            1,
            30,
            new OnObject(ItemId.Feathers, ObjectId.Fire),
            [new ItemStack(ItemId.Feathers, 1)],
            new Gives(new ItemStack(ItemId.BronzeHelmet, 1)));
        await StartAsync(Works, Only(recipe));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Feathers, 5);
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize - 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 4);

        Assert.Equal(["You have no room for that."], ana.Notices);
        Assert.Equal(5, Inventory("ana").CountOf(ItemId.Feathers));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeHelmet));
        Assert.Equal(0, Server.Skills.XpOf("ana", "cooking"));
    }

    [Fact]
    public async Task ARefusalWithNoRoomForWhatAFailureGivesChangesNothing()
    {
        var recipe = new Recipe(
            "cook the feathers",
            "cooking",
            1,
            30,
            new OnObject(ItemId.Feathers, ObjectId.Fire),
            [new ItemStack(ItemId.Feathers, 1)],
            new Gives(new ItemStack(ItemId.BronzeSword, 1)),
            failure: new Failure(1, 5, ItemId.BronzeHelmet));
        await StartAsync(Fails, Only(recipe));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Feathers, 5);
        Inventory("ana").Add(ItemId.Logs, Container.InventorySize - 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 4);

        Assert.Equal(["You have no room for that."], ana.Notices);
        Assert.Equal(5, Inventory("ana").CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task ARefusalWithOnlySomeOfTheInputsChangesNothing()
    {
        var recipe = new Recipe(
            "cook the feathers",
            "cooking",
            1,
            30,
            new OnObject(ItemId.RawFish, ObjectId.Fire),
            [new ItemStack(ItemId.RawFish, 1), new ItemStack(ItemId.Feathers, 1)],
            new Gives(new ItemStack(ItemId.CookedFish, 1)));
        await StartAsync(Works, Only(recipe));
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.RawFish, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 2);

        Assert.Equal(["You do not have what you need."], ana.Notices);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.RawFish));
    }

    [Fact]
    public async Task ARefusalIsToldOnTheTickOfTheClickAndQueuesNothing()
    {
        Server.Objects.Place(ObjectId.Fire, OpenWorld.SpawnTile);
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Tinderbox, 1);
        Inventory("ana").Add(ItemId.Logs, 1);

        await UseOnItemAsync(ana, 0, 1);
        await _test.TickAsync(2);

        Assert.Equal(["You cannot light a fire here."], ana.Notices);
    }

    [Fact]
    public async Task AnItemWithNoRecipeOnAFireDoesNothing()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Logs, 1);

        await UseOnAsync(ana, 0, _fire);
        await _test.TickAsync(MakeTicks * 2);

        Assert.Equal(["Nothing interesting happens."], ana.Notices);
        Assert.Equal(0, _rolls.Drawn);
    }

    // Lighting a fire.

    private async Task<TestClient> LighterAsync()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Tinderbox, 1);
        Inventory("ana").Add(ItemId.Logs, 2);
        return ana;
    }

    [Fact]
    public async Task LightingUsesTheLogsAndStandsAFireWhereThePlayerIs()
    {
        var ana = await LighterAsync();

        await UseOnItemAsync(ana, 0, 1);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Tinderbox));
        Assert.Equal(ObjectId.Fire, Server.Objects.At(OpenWorld.SpawnTile)?.Def.Id);
        Assert.Equal(40, Server.Skills.XpOf("ana", "firemaking"));
    }

    [Fact]
    public async Task ALitFireGoesOutAfterItsTicks()
    {
        var ana = await LighterAsync();
        await UseOnItemAsync(ana, 0, 1);
        await _test.TickAsync(MakeTicks + 1);
        var lit = Server.Objects.At(OpenWorld.SpawnTile)!;
        var ticks = ((Lights)DefaultRecipes.All.Recipes.First(recipe => recipe.Skill == "firemaking").Product).Ticks;

        await _test.TickAsync(ticks - 1);
        Assert.NotNull(Server.Objects.Find(lit.NetworkId));

        await _test.TickAsync();
        Assert.Null(Server.Objects.Find(lit.NetworkId));
        Assert.Null(Server.Objects.At(OpenWorld.SpawnTile));
    }

    [Fact]
    public async Task LightingIsRefusedWhereAFireAlreadyStands()
    {
        var ana = await LighterAsync();
        await UseOnItemAsync(ana, 0, 1);
        await _test.TickAsync(MakeTicks + 1);

        await UseOnItemAsync(ana, 0, 2);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal("You cannot light a fire here.", ana.Notices[^1]);
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Logs));
    }

    [Fact]
    public async Task ALitFireIsAStationThatCookingCanUse()
    {
        var ana = await LighterAsync();
        Inventory("ana").Add(ItemId.RawFish, 1);
        await UseOnItemAsync(ana, 0, 1);
        await _test.TickAsync(MakeTicks + 1);
        var lit = Server.Objects.At(OpenWorld.SpawnTile)!;

        await UseOnAsync(ana, 3, lit);
        await _test.TickAsync(MakeTicks + 4);

        Assert.Equal(1, Inventory("ana").CountOf(ItemId.CookedFish));
    }

    // The other recipes.

    [Fact]
    public async Task AKnifeCarvesLogsIntoArrowShaftsAndIsKept()
    {
        var ana = await _test.JoinAsync("Ana");
        Inventory("ana").Add(ItemId.Knife, 1);
        Inventory("ana").Add(ItemId.Logs, 1);

        await UseOnItemAsync(ana, 1, 0);
        await _test.TickAsync(MakeTicks + 1);

        Assert.Equal(15, Inventory("ana").CountOf(ItemId.ArrowShafts));
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.Logs));
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Knife));
        Assert.Equal(5, Server.Skills.XpOf("ana", "fletching"));
    }

    [Fact]
    public async Task AHammerForgesABarIntoASwordAtAnAnvil()
    {
        var ana = await _test.JoinAsync("Ana");
        Level("ana", "smithing", 4);
        Inventory("ana").Add(ItemId.Hammer, 1);
        Inventory("ana").Add(ItemId.BronzeBar, 1);
        var anvil = Server.Objects.Place(ObjectId.Anvil, At(33, 33));

        await UseOnAsync(ana, 1, anvil);
        await _test.TickAsync(MakeTicks + 2);
        Assert.Equal(0, Inventory("ana").CountOf(ItemId.BronzeSword));

        await _test.TickAsync();
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.BronzeSword));
        Assert.Equal(1, Inventory("ana").CountOf(ItemId.Hammer));
    }
}
