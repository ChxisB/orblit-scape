using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using Scape.Core;

namespace Scape.Content.Tests;

public class IslandParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_content", "island");

    [Fact]
    public void EveryRegionOfTheIslandHasTheFixturesFlags() => Golden.Check("region", c =>
    {
        var key = c.GetProperty("key");
        var region = StarterIsland.Build().Map.RegionAt(new RegionKey(key[0].GetInt32(), key[1].GetInt32(), key[2].GetInt32()));
        return region is null ? null : Dump.Text(Convert.ToHexStringLower(region.Flags));
    });

    [Fact]
    public void TheIslandPlacesWhatTheFixtureSays() => Golden.Check("plan", _ => Plan(StarterIsland.Build()));

    [Fact]
    public void EveryBrokenIslandHasTheFixturesProblems() =>
        Golden.Check("problems", c => Dump.Arr(ContentCheck.Problems(Broken(c)), Dump.Text));

    private static JsonArray Tile(TileCoord t) => [t.X, t.Y, t.Plane];

    private static JsonObject Plan(WorldPlan plan) => new()
    {
        ["spawn"] = Tile(plan.Spawn),
        ["objects"] = Dump.Arr(plan.Objects, p => new JsonArray(p.ObjectId, Tile(p.Tile), p.Destination is { } to ? Tile(to) : null)),
        ["npcs"] = Dump.Arr(plan.Npcs, s => new JsonArray(s.Npc, Tile(s.Tile), s.Radius)),
    };

    private static TileCoord TileOf(JsonElement t) => new(t[0].GetInt32(), t[1].GetInt32(), t[2].GetInt32());

    private static ObjectPlacement PlacementOf(JsonElement p) =>
        new(p[0].GetInt32(), TileOf(p[1]), p[2].ValueKind == JsonValueKind.Null ? null : TileOf(p[2]));

    private static NpcSpawn SpawnOf(JsonElement s) => new(s[0].GetInt32(), TileOf(s[1]), s[2].GetInt32());

    private static HashSet<int> Ints(JsonElement c, string name) => [.. c.GetProperty(name).EnumerateArray().Select(e => e.GetInt32())];

    private static HashSet<string> Texts(JsonElement c, string name) =>
        [.. c.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];

    /// <summary>The default data with the parts the case names dropped, added or changed.</summary>
    private static ContentSet Broken(JsonElement c)
    {
        var story = DefaultStory.Story;
        var dropRecipes = Texts(c, "dropRecipes");
        var dropDialogues = Texts(c, "dropDialogues");
        return new()
        {
            Items = DefaultItems.All,
            Objects = DefaultObjects.All,
            Nodes = DefaultNodes.All,
            Npcs = ShopWithout(Ints(c, "dropShopItems")),
            Recipes = new(
                [.. DefaultRecipes.All.Recipes.Where(r => !dropRecipes.Contains(r.Name))],
                DefaultItems.All,
                DefaultObjects.All,
                DefaultSkills.All),
            Story = new(
                [.. story.Book.Quests],
                [.. story.Dialogues.Where(d => !dropDialogues.Contains(d.Key))],
                story.Steps,
                story.Rewards),
            World = WorldOf(c),
        };
    }

    /// <summary>A fresh island, flagged, then with placements and spawns dropped by index and added.</summary>
    private static WorldPlan WorldOf(JsonElement c)
    {
        var plan = StarterIsland.Build();
        foreach (var f in c.GetProperty("flags").EnumerateArray())
        {
            plan.Map.SetFlags(TileOf(f), new TileFlags(f[3].GetInt32()));
        }

        var dropObjects = Ints(c, "dropObjects");
        var dropNpcs = Ints(c, "dropNpcs");
        var spawn = c.GetProperty("spawn");
        return new WorldPlan(
            plan.Map,
            spawn.ValueKind == JsonValueKind.Null ? plan.Spawn : TileOf(spawn),
            [
                .. plan.Objects.Where((_, i) => !dropObjects.Contains(i)),
                .. c.GetProperty("addObjects").EnumerateArray().Select(PlacementOf),
            ],
            [
                .. plan.Npcs.Where((_, i) => !dropNpcs.Contains(i)),
                .. c.GetProperty("addNpcs").EnumerateArray().Select(SpawnOf),
            ]);
    }

    private static NpcSet ShopWithout(HashSet<int> items)
    {
        var shop = DefaultNpcs.All.Require(NpcId.Shopkeeper);
        var lines = shop.Shop!.Lines.Where(l => !items.Contains(l.Item)).ToList();
        var without = new NpcDef(shop.Id, shop.Name, dialogue: shop.Dialogue, shop: new ShopDef(lines));
        return new([.. DefaultNpcs.All.Npcs.Select(npc => npc.Id == NpcId.Shopkeeper ? without : npc)]);
    }
}
