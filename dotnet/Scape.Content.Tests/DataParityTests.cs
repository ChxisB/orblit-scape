using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using Scape.Core;

namespace Scape.Content.Tests;

public class DataParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_content", "data");

    [Fact]
    public void TheSetsHoldWhatTheFixtureLists() => Golden.Check("sets", _ => Sets());

    [Fact]
    public void EveryItemMatchesTheFixture() => Golden.Check("item", c => Item(DefaultItems.All.Require(IdOf(c))));

    [Fact]
    public void EveryObjectMatchesTheFixture() => Golden.Check("object", c => Object(DefaultObjects.All.Require(IdOf(c))));

    [Fact]
    public void EveryNodeMatchesTheFixture() => Golden.Check("node", c => Node(DefaultNodes.All.Find(c.GetProperty("object").GetInt32())!));

    [Fact]
    public void EveryRecipeMatchesTheFixture() =>
        Golden.Check("recipe", c => Recipe(DefaultRecipes.All.Recipes.Single(r => r.Name == c.GetProperty("name").GetString())));

    [Fact]
    public void EveryCreatureMatchesTheFixture() => Golden.Check("npc", c => Npc(DefaultNpcs.All.Require(IdOf(c))));

    [Fact]
    public void EverySpellMatchesTheFixture() => Golden.Check("spell", c => Spell(DefaultSpells.All.Find(IdOf(c))!));

    [Fact]
    public void EveryPrayerMatchesTheFixture() => Golden.Check("prayer", c => Prayer(DefaultPrayers.All.Find(IdOf(c))!));

    [Fact]
    public void TheStoryMatchesTheFixture() => Golden.Check("story", _ => Story());

    private static int IdOf(JsonElement c) => c.GetProperty("id").GetInt32();

    private static string NameOf(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static JsonArray Stack(ItemStack stack) => [stack.Item, stack.Count];

    private static JsonObject Sets() => new()
    {
        ["curve"] = new JsonArray(DefaultSkills.All.Curve.Quadratic, DefaultSkills.All.Curve.Cubic),
        ["skills"] = Dump.Arr(DefaultSkills.All.Skills, s => new JsonArray(s.Id, s.Name, s.LevelCap)),
        ["items"] = Dump.Arr(DefaultItems.All.Items, i => i.Id),
        ["objects"] = Dump.Arr(DefaultObjects.All.Objects, o => o.Id),
        ["nodes"] = Dump.Arr(DefaultNodes.All.Nodes, n => n.ObjectId),
        ["recipes"] = Dump.Arr(DefaultRecipes.All.Recipes, r => r.Name),
        ["npcs"] = Dump.Arr(DefaultNpcs.All.Npcs, n => n.Id),
        ["spells"] = new JsonObject
        {
            ["skill"] = DefaultSpells.All.Skill,
            ["ids"] = Dump.Arr(DefaultSpells.All.Spells, s => s.Id),
        },
        ["prayers"] = new JsonObject
        {
            ["skill"] = DefaultPrayers.All.Skill,
            ["drainPerPoint"] = DefaultPrayers.All.DrainPerPoint,
            ["ids"] = Dump.Arr(DefaultPrayers.All.Prayers, p => p.Id),
        },
        ["combat"] = new JsonArray(DefaultCombat.Rules.FightingSkill, DefaultCombat.Rules.HealthSkill),
        ["shops"] = new JsonArray(DefaultShops.Rules.Coins, DefaultShops.Rules.BuyPercent, DefaultShops.Rules.SellPercent),
    };

    private static JsonObject Item(ItemDef item) => new()
    {
        ["name"] = item.Name,
        ["stackable"] = item.Stackable,
        ["value"] = item.Value,
        ["equipSlot"] = item.EquipSlot,
        ["needs"] = item.Needs is { } needs ? new JsonArray(needs.Skill, needs.Level) : null,
        ["combat"] = item.Combat is { } combat ? Bonus(combat) : null,
    };

    private static JsonObject Bonus(CombatBonus c) => new()
    {
        ["attack"] = c.Attack,
        ["strength"] = c.Strength,
        ["defence"] = c.Defence,
        ["speed"] = c.Speed,
        ["style"] = NameOf(c.Style),
        ["range"] = c.Range,
        ["ammo"] = c.Ammo,
    };

    private static JsonObject Object(ObjectDef o) => new()
    {
        ["name"] = o.Name,
        ["width"] = o.Width,
        ["height"] = o.Height,
        ["solid"] = o.Solid,
        ["options"] = Dump.Arr(o.Options, option => new JsonArray(option.Label, option.Action, option.WhenState)),
    };

    private static JsonObject Node(NodeDef n) => new()
    {
        ["skill"] = n.Skill,
        ["level"] = n.Level,
        ["tool"] = n.Tool,
        ["xp"] = n.Xp,
        ["product"] = n.Product,
        ["chanceLow"] = n.ChanceLow,
        ["chanceHigh"] = n.ChanceHigh,
        ["fullLevel"] = n.FullLevel,
        ["depleteChance"] = n.DepleteChance,
        ["respawnTicks"] = n.RespawnTicks,
    };

    private static JsonObject Recipe(Recipe r) => new()
    {
        ["skill"] = r.Skill,
        ["level"] = r.Level,
        ["xp"] = r.Xp,
        ["start"] = Start(r.Start),
        ["inputs"] = Dump.Arr(r.Inputs, Stack),
        ["product"] = Product(r.Product),
        ["tool"] = r.Tool,
        ["ticks"] = r.Ticks,
        ["failure"] = r.Failure is { } f ? new JsonArray(f.Chance, f.SafeLevel, f.Item) : null,
    };

    private static JsonObject Start(RecipeStart start) => start switch
    {
        OnItem s => new() { ["onItem"] = new JsonArray(s.Item, s.Target) },
        OnObject s => new() { ["onObject"] = new JsonArray(s.Item, s.ObjectId) },
        _ => throw new InvalidOperationException($"no start {start}"),
    };

    private static JsonObject Product(Product product) => product switch
    {
        Gives g => new() { ["gives"] = Stack(g.Stack) },
        Lights l => new() { ["lights"] = new JsonArray(l.ObjectId, l.Ticks) },
        _ => throw new InvalidOperationException($"no product {product}"),
    };

    private static JsonObject Npc(NpcDef n) => new()
    {
        ["name"] = n.Name,
        ["behaviour"] = Behaviour(n.Behaviour),
        ["fight"] = n.Fight is { } fight ? Fight(fight) : null,
        ["loot"] = n.Loot is { } loot ? Table(loot) : null,
        ["shop"] = n.Shop is { } shop ? Dump.Arr(shop.Lines, l => new JsonArray(l.Item, l.Cap, l.Start, l.RestockTicks)) : null,
        ["dialogue"] = n.Dialogue,
    };

    private static JsonNode Behaviour(Behaviour behaviour) => behaviour switch
    {
        Idle => "idle",
        Wander => "wander",
        Aggressive a => new JsonArray(a.Sight, a.Leash),
        _ => throw new InvalidOperationException($"no behaviour {behaviour}"),
    };

    private static JsonObject Fight(NpcFight f) => new()
    {
        ["health"] = f.Health,
        ["attack"] = f.Attack,
        ["strength"] = f.Strength,
        ["defence"] = f.Defence,
        ["speed"] = f.Speed,
        ["respawnTicks"] = f.RespawnTicks,
        ["style"] = NameOf(f.Style),
    };

    private static JsonObject Table(LootTable t) => new()
    {
        ["always"] = Dump.Arr(t.Always, Stack),
        ["entries"] = Dump.Arr(t.Entries, Entry),
        ["nothing"] = t.Nothing,
        ["rare"] = t.Rare is { } rare ? Table(rare) : null,
        ["rareOneIn"] = t.RareOneIn,
    };

    private static JsonNode Entry(LootEntry entry) => entry switch
    {
        LootItem i => new JsonArray(i.Weight, i.Item, i.Min, i.Max),
        LootSub s => new JsonObject { ["weight"] = s.Weight, ["table"] = Table(s.Table) },
        _ => throw new InvalidOperationException($"no entry {entry}"),
    };

    private static JsonObject Spell(SpellDef s) => new()
    {
        ["name"] = s.Name,
        ["level"] = s.Level,
        ["effect"] = Effect(s.Effect),
        ["cost"] = Dump.Arr(s.Cost, Stack),
        ["range"] = s.Range,
        ["speed"] = s.Speed,
        ["xp"] = s.Xp,
    };

    private static JsonObject Effect(SpellEffect effect) => effect switch
    {
        SpellDamage d => new() { ["damage"] = d.Most },
        SpellStun s => new() { ["stun"] = s.Ticks },
        SpellTeleport t => new() { ["teleport"] = new JsonArray(t.To.X, t.To.Y, t.To.Plane) },
        _ => throw new InvalidOperationException($"no effect {effect}"),
    };

    private static JsonArray Prayer(PrayerDef p) => [p.Name, p.Level, NameOf(p.Stat), p.Percent, p.Drain];

    private static JsonObject Story() => new()
    {
        ["quests"] = Dump.Arr(DefaultStory.Story.Book.Quests, q => JsonNode.Parse(q.Encode())),
        ["dialogues"] = Dump.Arr(DefaultStory.Story.Dialogues, d => JsonNode.Parse(d.Encode())),
        ["steps"] = new JsonObject(DefaultStory.Story.Steps.Select(
            p => KeyValuePair.Create(p.Key, (JsonNode?)new JsonArray(p.Value.Quest, p.Value.Stage)))),
        ["rewards"] = new JsonObject(DefaultStory.Story.Rewards.Select(
            p => KeyValuePair.Create(p.Key, (JsonNode?)Reward(p.Value)))),
    };

    private static JsonObject Reward(QuestReward r) => new()
    {
        ["xp"] = new JsonObject(r.Xp.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["items"] = Dump.Arr(r.Items, Stack),
    };
}
