using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using static Scape.Core.Tests.WorldSupport;

namespace Scape.Core.Tests;

public class WorldParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_core", "world");

    // Paths and sight.

    [Fact]
    public void PathsMatchTheFixture() => Golden.Check("path", PathOf);

    [Fact]
    public void LinesOfSightMatchTheFixture() => Golden.Check("sight", SightOf);

    // Region files.

    [Fact]
    public void RegionFilesMatchTheFixture() => Golden.Check("encode", Encoded);

    [Fact]
    public void ReadRegionFilesMatchTheFixture() => Golden.Check("decode", Decoded);

    // Tile positions.

    [Fact]
    public void TilePositionsMatchTheFixture() => Golden.Check("position", Positioned);

    [Fact]
    public void ReadPointsMatchTheFixture() => Golden.Check("read", ReadPoint);

    [Fact]
    public void NearestTilesMatchTheFixture() => Golden.Check("nearest", c => Dump.Attempt(() => Nearest(c)));

    private static TileCoord Tile(JsonElement xy) => At(xy[0].GetInt32(), xy[1].GetInt32());

    private static JsonArray Xy(TileCoord tile) => new(tile.X, tile.Y);

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static TileMap MapOf(JsonElement c) =>
        WindowMap([.. c.GetProperty("window").EnumerateArray().Select(bits => new TileFlags(bits.GetInt32()))]);

    private static Reach ReachOf(JsonElement row)
    {
        var origin = Tile(row.GetProperty("origin"));
        var target = new Footprint(origin, row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32());
        return row.GetProperty("kind").GetString() switch
        {
            "on" => new OnTile(origin),
            "beside" => new Beside(target),
            "within" => new Within(target, row.GetProperty("range").GetInt32()),
            var kind => throw new InvalidOperationException($"No reach is called {kind}."),
        };
    }

    private static JsonObject PathOf(JsonElement c)
    {
        var result = PathSearch.FindToReach(
            MapOf(c),
            Tile(c.GetProperty("from")),
            ReachOf(c.GetProperty("reach")),
            Enum.Parse<Walker>(c.GetProperty("walker").GetString()!, ignoreCase: true),
            c.GetProperty("margin").GetInt32());
        return new JsonObject { ["reached"] = result.Reached, ["steps"] = Dump.Arr(result.Steps, Xy) };
    }

    private static JsonArray SightOf(JsonElement c)
    {
        var map = MapOf(c);
        return Dump.Arr(
            c.GetProperty("pairs").EnumerateArray(),
            p => JsonValue.Create(LineOfSight.HasClearLine(map, At(p[0].GetInt32(), p[1].GetInt32()), At(p[2].GetInt32(), p[3].GetInt32()))));
    }

    private static JsonNode Encoded(JsonElement c)
    {
        var key = c.GetProperty("key");
        var region = FlagRegion.Open(new RegionKey(key[0].GetInt32(), key[1].GetInt32(), key[2].GetInt32()));
        foreach (var set in c.GetProperty("set").EnumerateArray())
        {
            region.SetLocal(set[0].GetInt32(), new TileFlags(set[1].GetInt32()));
        }

        return Dump.Text(Hex(FlagRegionFile.Encode(region)));
    }

    /// <summary>
    /// What reading an open region's file with the case's edits gave, or the
    /// message it was refused with. The messages are the reader's own words,
    /// so they are compared.
    /// </summary>
    private static JsonObject Decoded(JsonElement c)
    {
        var bytes = FlagRegionFile.Encode(FlagRegion.Open(new RegionKey(0, 0, 0)));
        foreach (var edit in c.GetProperty("edits").EnumerateArray())
        {
            bytes[edit[0].GetInt32()] = edit[1].GetByte();
        }

        try
        {
            var region = FlagRegionFile.Decode(bytes.AsSpan(0, c.GetProperty("length").GetInt32()));
            var key = region.Key;
            return new JsonObject { ["value"] = new JsonArray(key.X, key.Y, key.Plane, region.Flags[0], region.Flags[4095]) };
        }
        catch (FormatException e)
        {
            return new JsonObject { ["refused"] = e.Message };
        }
    }

    private static JsonNode Positioned(JsonElement c)
    {
        var tile = c.GetProperty("tile");
        var bytes = new byte[TilePosition.Floats * 4];
        TilePosition.Write(bytes, new TileCoord(tile[0].GetInt32(), tile[1].GetInt32(), tile[2].GetInt32()));
        return Dump.Text(Hex(bytes));
    }

    private static JsonArray ReadPoint(JsonElement c)
    {
        var point = TilePosition.Read(Convert.FromHexString(c.GetProperty("bytes").GetString()!));
        return new JsonArray(Dump.Num(point.X), Dump.Num(point.Y), point.Plane);
    }

    private static JsonArray Nearest(JsonElement c)
    {
        var point = c.GetProperty("point");
        return Xy(TilePosition.Nearest(new TilePoint(Fixture.Number(point[0]), Fixture.Number(point[1]), 0)));
    }
}
