using Orblit.Dialogue;
using Scape.Core;

namespace Scape.Content.Tests;

public class CheckTests
{
    /// <summary>The default data on a fresh island. A test swaps one part for a broken one with <c>with</c>.</summary>
    private static ContentSet Defaults() => new()
    {
        Items = DefaultItems.All,
        Objects = DefaultObjects.All,
        Nodes = DefaultNodes.All,
        Npcs = DefaultNpcs.All,
        Recipes = DefaultRecipes.All,
        Story = DefaultStory.Story,
        World = StarterIsland.Build(),
    };

    private static IReadOnlyList<string> Problems(Func<ContentSet, ContentSet> broken) => ContentCheck.Problems(broken(Defaults()));

    /// <summary>A fresh island with some parts replaced. The map is the island's own.</summary>
    private static WorldPlan Island(
        TileCoord? spawn = null,
        Func<IReadOnlyList<ObjectPlacement>, IEnumerable<ObjectPlacement>>? objects = null,
        Func<IReadOnlyList<NpcSpawn>, IEnumerable<NpcSpawn>>? npcs = null)
    {
        var plan = StarterIsland.Build();
        return new WorldPlan(
            plan.Map,
            spawn ?? plan.Spawn,
            [.. objects?.Invoke(plan.Objects) ?? plan.Objects],
            [.. npcs?.Invoke(plan.Npcs) ?? plan.Npcs]);
    }

    private static WorldPlan Without(Func<ObjectPlacement, bool> drop) => Island(objects: all => all.Where(p => !drop(p)));

    private static WorldPlan WithNpcs(params NpcSpawn[] extra) => Island(npcs: all => [.. all, .. extra]);

    private static WorldPlan WithObjects(params ObjectPlacement[] extra) => Island(objects: all => [.. all, .. extra]);

    private static TileCoord At(int x, int y, int plane = 0) => new(x, y, plane);

    private static TileCoord FirstTree() => StarterIsland.Build().Objects.First(p => p.ObjectId == ObjectId.Tree).Tile;

    /// <summary>The default story with its dialogues swapped for <paramref name="dialogues"/>.</summary>
    private static Story StoryWith(IReadOnlyList<DialogueDefinition> dialogues)
    {
        var story = DefaultStory.Story;
        return new Story([.. story.Book.Quests], dialogues, story.Steps, story.Rewards);
    }

    private static List<DialogueDefinition> DialoguesWithout(string key) =>
        [.. DefaultStory.Story.Dialogues.Where(d => d.Key != key)];

    private static NpcSet NpcsWith(NpcDef extra) => new([.. DefaultNpcs.All.Npcs, extra]);

    private static NpcSet ShopWithout(int item)
    {
        var shop = DefaultNpcs.All.Require(NpcId.Shopkeeper);
        var lines = shop.Shop!.Lines.Where(l => l.Item != item).ToList();
        var without = new NpcDef(shop.Id, shop.Name, dialogue: shop.Dialogue, shop: new ShopDef(lines));
        return new([.. DefaultNpcs.All.Npcs.Select(npc => npc.Id == NpcId.Shopkeeper ? without : npc)]);
    }

    private static RecipeSet RecipesWith(IEnumerable<Recipe> recipes) =>
        new([.. recipes], DefaultItems.All, DefaultObjects.All, DefaultSkills.All);

    private static bool StartsWith(string line, string start) => line.StartsWith(start, StringComparison.Ordinal);

    [Fact]
    public void TheDefaultDataAgreesWithItself()
    {
        Assert.Empty(ContentCheck.Problems(Defaults()));
    }

    [Fact]
    public void TheIslandHasFiveRegionsAndAnOpenSpawnTile()
    {
        var island = StarterIsland.Build();

        Assert.Equal(5, island.Map.RegionCount);
        Assert.Equal(TileFlags.Open, island.Map.FlagsAt(island.Spawn));
    }

    [Fact]
    public void TwoCallsGiveTwoMapsSinceTheServerWritesToItsOwn()
    {
        var a = StarterIsland.Build();
        var b = StarterIsland.Build();
        a.Map.SetFlags(a.Spawn, TileFlags.Blocked);

        Assert.Equal(TileFlags.Open, b.Map.FlagsAt(b.Spawn));
    }

    [Fact]
    public void TheGuardCountsTheFourGoblinsThatAreThere()
    {
        var goblins = StarterIsland.Build().Npcs.Where(s => s.Npc == NpcId.Goblin);

        Assert.Equal(4, goblins.Count());
    }

    [Fact]
    public void AFishingSpotStandsBesideWater()
    {
        var island = StarterIsland.Build();
        foreach (var p in island.Objects.Where(p => p.ObjectId == ObjectId.FishingSpot))
        {
            (int Dx, int Dy)[] sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];
            var beside = sides.Select(d => island.Map.FlagsAt(At(p.Tile.X + d.Dx, p.Tile.Y + d.Dy)));

            Assert.True(beside.Any(f => f.Has(TileFlags.Water)), p.ToString());
        }
    }

    // Names that nothing answers.

    [Fact]
    public void ACreatureOpensADialogueTheStoryLacks()
    {
        var found = Problems(c => c with
        {
            Npcs = NpcsWith(new NpcDef(6, "Ghost", dialogue: "ghost")),
            World = WithNpcs(new NpcSpawn(6, At(62, 58))),
        });

        Assert.Contains("Ghost opens dialogue 'ghost', which does not exist.", found);
    }

    [Fact]
    public void ADialogueRunsAStepTheStoryDoesNotDefine()
    {
        var stray = DialogueDefinition.Parse("""
            {"kind": "orblit.dialogue", "formatVersion": 1, "key": "guard",
             "variants": [{"start": "a"}],
             "nodes": {"a": {"type": "line", "speaker": "G", "text": "Hi", "before": "nope"}}}
            """);
        var found = Problems(c => c with { Story = StoryWith([.. DialoguesWithout("guard"), stray]) });

        Assert.Contains("Dialogue 'guard' runs step 'nope', which the story does not define.", found);
    }

    [Fact]
    public void AStageThatNoDialogueReachesCannotBeFinished()
    {
        var found = Problems(c => c with { Story = StoryWith(DialoguesWithout("guard")) });

        Assert.Contains("Quest 'tally' stage 2 is reached by no step that a dialogue runs.", found);
    }

    [Fact]
    public void ADialogueThatNoCreatureOpens()
    {
        var extra = DialogueDefinition.Parse("""
            {"kind": "orblit.dialogue", "formatVersion": 1, "key": "extra",
             "variants": [{"start": "a"}],
             "nodes": {"a": {"type": "line", "speaker": "X", "text": "Hi"}}}
            """);
        var found = Problems(c => c with { Story = StoryWith([.. DefaultStory.Story.Dialogues, extra]) });

        Assert.Equal(["Dialogue 'extra' is opened by no creature."], found);
    }

    [Fact]
    public void ACreatureThatIsNeverSpawned()
    {
        var found = Problems(c => c with { Npcs = NpcsWith(new NpcDef(6, "Ghost")) });

        Assert.Equal(["Ghost is never spawned."], found);
    }

    [Fact]
    public void ALootTableDropsAnItemTheGameLacks()
    {
        var found = Problems(c => c with
        {
            Npcs = NpcsWith(new NpcDef(6, "Crab", loot: new LootTable(always: [new ItemStack(900, 1)]))),
            World = WithNpcs(new NpcSpawn(6, At(62, 58))),
        });

        Assert.Contains("Crab drops item 900, which does not exist.", found);
    }

    // What cannot be got.

    [Fact]
    public void ARecipeWhoseStationStandsNowhere()
    {
        var found = Problems(c => c with { World = Without(p => p.ObjectId == ObjectId.Anvil) });

        Assert.Contains("Anvil is never placed and no recipe lights it.", found);
    }

    [Fact]
    public void ARecipeToolThatNoShopSells()
    {
        var found = Problems(c => c with { Npcs = ShopWithout(ItemId.Hammer) });

        Assert.Contains(found, p => StartsWith(p, "Nothing gives Hammer."));
    }

    [Fact]
    public void ANodeToolThatNoShopSells()
    {
        var found = Problems(c => c with { Npcs = ShopWithout(ItemId.FishingNet) });

        Assert.Contains(found, p => StartsWith(p, "Nothing gives Fishing net."));

        // The spots need the net, and the fire needs the fish.
        Assert.Contains(found, p => StartsWith(p, "Nothing gives Raw fish."));
    }

    [Fact]
    public void ARecipeThatNeedsItsOwnProduct()
    {
        var recipes = RecipesWith(DefaultRecipes.All.Recipes.Select(r => r.Name == "smelt the bar"
            ? new Recipe(
                r.Name,
                r.Skill,
                r.Level,
                r.Xp,
                new OnObject(ItemId.BronzeBar, ObjectId.Furnace),
                [new ItemStack(ItemId.BronzeBar, 1)],
                new Gives(new ItemStack(ItemId.BronzeBar, 1)))
            : r));

        Assert.Contains(Problems(c => c with { Recipes = recipes }), p => StartsWith(p, "Nothing gives Bronze bar."));
    }

    [Fact]
    public void AnInputThatNoRecipeMakes()
    {
        var recipes = RecipesWith(DefaultRecipes.All.Recipes.Where(r => r.Name != "smelt the bar"));

        Assert.Contains(Problems(c => c with { Recipes = recipes }), p => StartsWith(p, "Nothing gives Bronze bar."));
    }

    [Fact]
    public void AnInputMadeFromAMadeInputCanBeGot()
    {
        // The net catches the fish, the fire cooks it and the furnace takes it,
        // so the bar is three makes from what the shop sells.
        var recipes = RecipesWith(DefaultRecipes.All.Recipes.Select(r => r.Name == "smelt the bar"
            ? new Recipe(
                r.Name,
                r.Skill,
                r.Level,
                r.Xp,
                new OnObject(ItemId.CookedFish, ObjectId.Furnace),
                [new ItemStack(ItemId.CookedFish, 1)],
                r.Product)
            : r));

        Assert.Empty(Problems(c => c with { Recipes = recipes }));
    }

    // The ground.

    [Fact]
    public void ACreatureInTheSeaAndOneInsideATree()
    {
        var tree = FirstTree();
        var found = Problems(c => c with
        {
            World = WithNpcs(new NpcSpawn(NpcId.Goblin, At(1, 1)), new NpcSpawn(NpcId.Goblin, tree)),
        });

        Assert.Contains("Goblin at (1, 1, 0) stands on ground it cannot use.", found);
        Assert.Contains($"Goblin at ({tree.X}, {tree.Y}, 0) stands inside a solid object.", found);
    }

    [Fact]
    public void ACreatureOnANoCreatureTileAndOneOnStairs()
    {
        var plan = Island();
        plan.Map.SetFlags(At(62, 58), TileFlags.NoNpc);
        var stairs = plan.Objects.First(p => p.ObjectId == ObjectId.StairsUp);
        var world = new WorldPlan(
            plan.Map,
            plan.Spawn,
            plan.Objects,
            [.. plan.Npcs, new NpcSpawn(NpcId.Goblin, At(62, 58)), new NpcSpawn(NpcId.Goblin, stairs.Tile)]);

        Assert.Equal(["Goblin at (62, 58, 0) stands on ground it cannot use."], Problems(c => c with { World = world }));
    }

    [Fact]
    public void ACreatureThatHasNoDefinition()
    {
        var found = Problems(c => c with { World = WithNpcs(new NpcSpawn(99, At(62, 58))) });

        Assert.Equal(["Creature 99 at (62, 58, 0) does not exist."], found);
    }

    [Fact]
    public void AnObjectOnTheWaterAndTwoOnOneTile()
    {
        var found = Problems(c => c with
        {
            World = WithObjects(new ObjectPlacement(ObjectId.Rock, At(1, 1)), new ObjectPlacement(ObjectId.Rock, At(100, 60))),
        });

        Assert.Contains("Rock at (1, 1, 0) stands on blocked ground.", found);
        Assert.Contains("Rock at (100, 60, 0) overlaps Rock.", found);
    }

    [Fact]
    public void AnObjectThatRunsOffTheWorldIsALineNotACrash()
    {
        var wide = new ObjectDef(99, "Hall", width: 2, height: 2);
        var edge = At(TileWorld.WorldSize - 1, TileWorld.WorldSize - 1);
        var found = Problems(c => c with
        {
            Objects = new ObjectSet([.. DefaultObjects.All.Objects, wide]),
            World = WithObjects(new ObjectPlacement(99, edge)),
        });

        Assert.Contains("Hall at (16383, 16383, 0) runs off the world.", found);
    }

    [Fact]
    public void StairsWithNoWay()
    {
        var world = Island(objects: all =>
            [.. all.Where(p => p.ObjectId != ObjectId.StairsUp), new ObjectPlacement(ObjectId.StairsUp, At(69, 85))]);

        Assert.Contains("Stairs at (69, 85, 0) leads nowhere.", Problems(c => c with { World = world }));
    }

    [Fact]
    public void StairsIntoAWallAndStairsIntoATree()
    {
        var tree = FirstTree();
        var world = WithObjects(
            new ObjectPlacement(ObjectId.StairsDown, At(60, 60), Destination: At(1, 1)),
            new ObjectPlacement(ObjectId.StairsDown, At(58, 58), Destination: tree));

        Assert.Equal(
            [
                "Stairs at (60, 60, 0) leads to blocked ground at (1, 1, 0).",
                $"Stairs at (58, 58, 0) leads into a solid object at ({tree.X}, {tree.Y}, 0).",
            ],
            Problems(c => c with { World = world }));
    }

    [Fact]
    public void ASpawnTileInTheSeaAndOneInsideATree()
    {
        var tree = FirstTree();

        Assert.Contains("The spawn tile (1, 1, 0) is blocked.", Problems(c => c with { World = Island(spawn: At(1, 1)) }));
        Assert.Contains(
            $"The spawn tile ({tree.X}, {tree.Y}, 0) is inside a solid object.",
            Problems(c => c with { World = Island(spawn: tree) }));
    }

    // Reach.

    [Fact]
    public void ARoomWithTheDoorWalledUpCannotBeEntered()
    {
        var plan = Island();
        plan.Map.SetFlags(At(56, 62), TileFlags.WallSouth | TileFlags.WallEast | TileFlags.WallWest);

        Assert.Contains(
            "A creature at (56, 65, 0) cannot be reached from the spawn tile.",
            Problems(c => c with { World = plan }));
    }

    [Fact]
    public void ABankBoothBehindAWallIsNotReachedFromOutsideIt()
    {
        var plan = Island();
        plan.Map.SetFlags(At(71, 62), TileFlags.WallSouth | TileFlags.WallEast | TileFlags.WallWest);
        var found = Problems(c => c with { World = plan });

        Assert.Contains("Bank booth at (70, 66, 0) cannot be reached from the spawn tile.", found);
        Assert.Contains("Bank booth at (72, 66, 0) cannot be reached from the spawn tile.", found);
    }

    [Fact]
    public void RocksStandingRoundATileShutItOff()
    {
        (int X, int Y)[] ring = [(61, 56), (62, 56), (63, 56), (61, 57), (63, 57), (61, 58), (62, 58), (63, 58)];
        var world = Island(
            objects: all => [.. all, .. ring.Select(t => new ObjectPlacement(ObjectId.Rock, At(t.X, t.Y)))],
            npcs: all => [.. all.Where(s => s.Npc != NpcId.Guard), new NpcSpawn(NpcId.Guard, At(62, 57))]);

        Assert.Equal(
            ["A creature at (62, 57, 0) cannot be reached from the spawn tile."],
            Problems(c => c with { World = world }));
    }

    [Fact]
    public void WithoutTheStairsTheFloorAboveIsOutOfReach()
    {
        var found = Problems(c => c with { World = Without(p => p.ObjectId == ObjectId.StairsUp) });

        Assert.Contains("Stairs at (69, 85, 1) cannot be reached from the spawn tile.", found);
    }
}
