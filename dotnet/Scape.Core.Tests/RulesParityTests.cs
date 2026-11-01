using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;

namespace Scape.Core.Tests;

public class RulesParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_core", "rules");

    // Levels.

    [Fact]
    public void ExperienceCurvesMatchTheFixture() => Golden.Check("curve", Curve);

    // Fights.

    [Fact]
    public void HitChancesAndMaxHitsMatchTheFixture() => Golden.Check("hit", Hit);

    [Fact]
    public void SwingsUnderTheSameDiceMatchTheFixture() => Golden.Check("swing", Swing);

    // Loot.

    [Fact]
    public void LootRollsUnderTheSameDiceMatchTheFixture() => Golden.Check("loot", Rolls);

    // Actor components.

    [Fact]
    public void WrittenAnimsMatchTheFixture() => Golden.Check("anim", AnimWritten);

    [Fact]
    public void ReadAnimsMatchTheFixture() => Golden.Check("animRead", AnimRead);

    [Fact]
    public void WrittenLooksMatchTheFixture() => Golden.Check("look", LookWritten);

    [Fact]
    public void ReadLooksMatchTheFixture() => Golden.Check("lookRead", LookRead);

    private static JsonObject Curve(JsonElement c)
    {
        var curve = new XpCurve(c.GetProperty("quadratic").GetInt32(), c.GetProperty("cubic").GetInt32());
        var cap = c.GetProperty("cap").GetInt32();
        return new JsonObject
        {
            ["costs"] = Dump.Arr(Enumerable.Range(1, cap), level => curve.XpForLevel(level)),
            ["levels"] = Dump.Arr(c.GetProperty("xps").EnumerateArray(), xp => curve.LevelOf(xp.GetInt64(), cap)),
        };
    }

    private static CombatRules RulesOf(JsonElement c)
    {
        var row = c.GetProperty("rules");
        return new CombatRules(
            "fighting",
            "health",
            effortBase: row.GetProperty("effortBase").GetInt32(),
            gearBase: row.GetProperty("gearBase").GetInt32(),
            hitDivisor: row.GetProperty("hitDivisor").GetInt32());
    }

    private static Rating RatingOf(JsonElement c, string name)
    {
        var row = c.GetProperty(name);
        return new Rating(row[0].GetInt32(), row[1].GetInt32());
    }

    private static double EdgeOf(JsonElement c) => Fixture.Number(c.GetProperty("edge"));

    private static JsonObject Hit(JsonElement c)
    {
        var rules = RulesOf(c);
        return new JsonObject
        {
            ["chance"] = Dump.Num(rules.HitChance(RatingOf(c, "attack"), RatingOf(c, "defence"), EdgeOf(c))),
            ["most"] = rules.MaxHit(RatingOf(c, "strength")),
        };
    }

    private static JsonNode Swing(JsonElement c)
    {
        var dice = new Replay(c.GetProperty("dice"));
        var damage = RulesOf(c).Swing(dice, RatingOf(c, "attack"), RatingOf(c, "strength"), RatingOf(c, "defence"), EdgeOf(c));
        dice.AssertSpent();
        return damage;
    }

    private static LootTable TableOf(JsonElement row) => new(
        [.. row.GetProperty("always").EnumerateArray().Select(StackOf)],
        [.. row.GetProperty("entries").EnumerateArray().Select(EntryOf)],
        row.GetProperty("nothing").GetInt32(),
        row.GetProperty("rare") is { ValueKind: JsonValueKind.Object } rare ? TableOf(rare) : null,
        row.GetProperty("rareOneIn").GetInt32());

    private static LootEntry EntryOf(JsonElement row)
    {
        var weight = row.GetProperty("weight").GetInt32();
        return row.TryGetProperty("table", out var table)
            ? new LootSub(weight, TableOf(table))
            : new LootItem(weight, row.GetProperty("item").GetInt32(), row.GetProperty("min").GetInt32(), row.GetProperty("max").GetInt32());
    }

    private static ItemStack StackOf(JsonElement row) => new(row[0].GetInt32(), row[1].GetInt32());

    private static JsonArray Stacks(IEnumerable<ItemStack> stacks) =>
        Dump.Arr(stacks, stack => new JsonArray(stack.Item, stack.Count));

    private static JsonArray Rolls(JsonElement c)
    {
        var table = TableOf(c.GetProperty("table"));
        var dice = new Replay(c.GetProperty("dice"));
        var rolls = new JsonArray();
        for (var roll = 0; roll < 4; roll++)
        {
            rolls.Add(Stacks(table.Roll(dice)));
        }

        dice.AssertSpent();
        return rolls;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static ActorState StateOf(JsonElement name) => Enum.Parse<ActorState>(name.GetString()!, ignoreCase: true);

    private static JsonNode NameOf(ActorState state) => Dump.Text(JsonNamingPolicy.CamelCase.ConvertName(state.ToString()));

    private static int[] Ints(JsonElement c) => [.. c.GetProperty("ints").EnumerateArray().Select(value => value.GetInt32())];

    private static byte[] BytesOf(int[] ints)
    {
        var bytes = new byte[ints.Length * 4];
        for (var index = 0; index < ints.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), ints[index]);
        }

        return bytes;
    }

    private static JsonNode AnimWritten(JsonElement c)
    {
        var act = c.GetProperty("act") is { ValueKind: JsonValueKind.String } name ? StateOf(name) : (ActorState?)null;
        var bytes = new byte[Anim.Ints * 4];
        Anim.Write(bytes, new Anim(StateOf(c.GetProperty("base")), act, c.GetProperty("seq").GetInt32()));
        return Dump.Text(Hex(bytes));
    }

    private static JsonArray? AnimRead(JsonElement c) =>
        Anim.TryRead(BytesOf(Ints(c))) is { } anim
            ? new JsonArray(NameOf(anim.Base), anim.Act is { } act ? NameOf(act) : null, anim.Seq)
            : null;

    private static int[] LookInts(Look look) => [look.Skin, look.Hair, look.Cloth, .. look.Worn];

    private static JsonNode LookWritten(JsonElement c)
    {
        var ints = Ints(c);
        var bytes = new byte[Look.Ints * 4];
        Look.Write(bytes, new Look(ints[0], ints[1], ints[2], ints[Look.Colours..]));
        return Dump.Text(Hex(bytes));
    }

    private static JsonArray? LookRead(JsonElement c) =>
        Look.TryRead(BytesOf(Ints(c))) is { } look ? Dump.Arr(LookInts(look), value => value) : null;

    /// <summary>
    /// Plays back the draws the exporter wrote down, in order. Each draw names the bound it was
    /// made under, so a port that asks a different question fails at that draw.
    /// </summary>
    private sealed class Replay(JsonElement draws) : Random
    {
        private readonly JsonElement[] _draws = [.. draws.EnumerateArray()];
        private int _at;

        public override int Next(int maxValue)
        {
            var draw = _draws[_at++];
            Assert.Equal(draw.GetProperty("below").GetInt32(), maxValue);
            return draw.GetProperty("gave").GetInt32();
        }

        public override double NextDouble()
        {
            var draw = _draws[_at++];
            Assert.False(draw.TryGetProperty("below", out _), "the Dart drew an int here");
            return Fixture.Number(draw.GetProperty("gave"));
        }

        public void AssertSpent() => Assert.Equal(_draws.Length, _at);
    }
}
