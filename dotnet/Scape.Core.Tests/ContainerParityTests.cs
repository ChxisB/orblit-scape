using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class ContainerParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_core", "containers");

    // A list of cases is data: each step's name picks its call.
    private static readonly Dictionary<string, Func<Step, JsonNode?>> Calls = new()
    {
        ["add"] = step => Dump.Attempt(() => step.Bag.Add(step.Int("item"), step.Int("count"), step.Maybe("tab"))),
        ["take"] = step => StackRow(step.Bag.Take(step.Int("slot"), step.Maybe("count"))),
        ["move"] = step => NameOf(step.Bag.Move(step.Int("from"), step.Int("to"))),
        ["swap"] = step => NameOf(step.Bag.Swap(step.Int("a"), step.Int("b"))),
        ["swapWith"] = step => NameOf(step.Bag.SwapWith(step.BagAt("other"), step.Int("slot"), step.Int("otherSlot"))),
        ["stack"] = step => NameOf(step.Bag.Stack(step.Int("from"), step.Int("to"))),
        ["split"] = step => NameOf(step.Bag.Split(step.Int("from"), step.Int("to"), step.Int("count"))),
        ["transfer"] = step =>
            NameOf(step.Bag.Transfer(step.Int("slot"), step.BagAt("into"), step.Maybe("count"), step.Maybe("tab"))),
        ["exchange"] = step => Dump.Attempt(() => NameOf(step.Bag.Exchange(step.Stacks("take"), step.Stacks("give")))),
        ["tryExchange"] = step =>
            Dump.Attempt(() => NameOf(step.Bag.TryExchange(step.Stacks("take"), step.Stacks("give")))),
        ["canHold"] = step => Dump.Attempt(() => step.Bag.CanHold(step.Int("item"))),
        ["countOf"] = step => step.Bag.CountOf(step.Int("item")),
        ["search"] = step => Dump.Arr(step.Bag.Search(step.Text("text"), step.Maybe("tab")), slot => slot),
        ["markClean"] = step =>
        {
            step.Bag.MarkClean();
            return null;
        },
        ["mirror"] = step =>
        {
            step.Bag.Mirror([new SlotChange(step.Int("slot"), StackOf(step.Row.GetProperty("stack")))]);
            return null;
        },
    };

    [Fact]
    public void ScriptedMovesMatchTheFixture() => Golden.Check("script", Script);

    [Fact]
    public void RestoredSavesMatchTheFixture() => Golden.Check("restore", Restore);

    private static Container[] Bags() => [.. Enum.GetValues<ContainerId>().Select(id => Container.ForId(id, Items()))];

    private static JsonObject Script(JsonElement c)
    {
        var bags = Bags();
        var results = Dump.Arr(
            c.GetProperty("steps").EnumerateArray(),
            row => Calls[row.GetProperty("op").GetString()!](new Step(row, bags)));
        return new JsonObject { ["results"] = results, ["bags"] = Dump.Arr(bags, BagRow) };
    }

    private static JsonObject Restore(JsonElement c)
    {
        var bag = Bags()[c.GetProperty("in").GetInt32()];
        var lost = bag.Restore([.. c.GetProperty("saved").EnumerateArray().Select(StackOf)]);
        return new JsonObject { ["lost"] = lost, ["slots"] = SlotRows(bag), ["dirty"] = DirtyRow(bag) };
    }

    private static JsonObject BagRow(Container bag) => new() { ["slots"] = SlotRows(bag), ["dirty"] = DirtyRow(bag) };

    private static JsonArray SlotRows(Container bag) =>
        Dump.Arr(bag.Occupied(), slot => bag.At(slot) is { } stack ? new JsonArray(slot, stack.Item, stack.Count) : null);

    private static JsonArray DirtyRow(Container bag) => Dump.Arr(bag.Dirty, slot => slot);

    private static JsonArray? StackRow(ItemStack? stack) =>
        stack is { } held ? new JsonArray(held.Item, held.Count) : null;

    private static ItemStack? StackOf(JsonElement row) =>
        row.ValueKind == JsonValueKind.Null ? null : new ItemStack(row[0].GetInt32(), row[1].GetInt32());

    private static JsonNode NameOf(Outcome outcome) => Dump.Text(JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString()));

    /// <summary>One scripted step: its row of arguments and the containers it may name.</summary>
    private sealed class Step(JsonElement row, Container[] bags)
    {
        public JsonElement Row => row;

        /// <summary>The container the step acts on.</summary>
        public Container Bag => bags[Int("in")];

        public Container BagAt(string name) => bags[Int(name)];

        public int Int(string name) => row.GetProperty(name).GetInt32();

        public int? Maybe(string name) =>
            row.GetProperty(name) is { ValueKind: JsonValueKind.Number } value ? value.GetInt32() : null;

        public string Text(string name) => row.GetProperty(name).GetString()!;

        public ItemStack[] Stacks(string name) =>
            [.. row.GetProperty(name).EnumerateArray().Select(stack => new ItemStack(stack[0].GetInt32(), stack[1].GetInt32()))];
    }
}
