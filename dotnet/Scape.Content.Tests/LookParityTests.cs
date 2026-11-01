using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using Scape.Core;

namespace Scape.Content.Tests;

public class LookParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_content", "look");

    [Fact]
    public void EveryActLastsWhatTheFixtureSays() => Golden.Check("act", c => Dump.Num(Poses.ActSeconds(StateOf(c))));

    [Fact]
    public void EveryPoseOnTheTimeGridMatchesTheFixture() =>
        Golden.Check("pose", c => Pose(Poses.Of(StateOf(c), c.GetProperty("seconds").GetDouble())));

    [Fact]
    public void TheCreaturesWithAModelAreTheFixtures() => Golden.Check("npcIds", _ => Dump.Arr(DefaultModels.All.NpcIds, id => id));

    [Fact]
    public void EveryCreatureModelMatchesTheFixture() =>
        Golden.Check("npc", c => Model(DefaultModels.All.Npc(c.GetProperty("id").GetInt32())));

    [Fact]
    public void EveryPlayerModelMatchesTheFixture() => Golden.Check("player", c => Model(DefaultModels.All.Player(LookOf(c))));

    private static string NameOf(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static ActorState StateOf(JsonElement c) => Enum.Parse<ActorState>(c.GetProperty("state").GetString()!, ignoreCase: true);

    private static Look LookOf(JsonElement c) => new(
        c.GetProperty("skin").GetInt32(),
        c.GetProperty("hair").GetInt32(),
        c.GetProperty("cloth").GetInt32(),
        [.. c.GetProperty("worn").EnumerateArray().Select(w => w.GetInt32())]);

    private static JsonArray Pose(Pose p) => [p.LegL, p.LegR, p.ArmL, p.ArmR, p.Lean, p.Lie];

    private static JsonArray Vec(Vec3 v) => [v.X, v.Y, v.Z];

    private static JsonObject? Model(ActorModel? model) => model is null ? null : new()
    {
        ["name"] = model.Name,
        ["boxes"] = Dump.Arr(model.Boxes, b => new JsonArray(NameOf(b.Bone), Vec(b.At), Vec(b.Size), b.Colour)),
        ["pivots"] = new JsonObject(model.Pivots.Select(p => KeyValuePair.Create(NameOf(p.Key), (JsonNode?)Vec(p.Value)))),
    };
}
