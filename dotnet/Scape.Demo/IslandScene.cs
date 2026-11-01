using Orblit.Filament;
using Orblit.VectorMath;
using Scape.Client;
using Scape.Content;
using Scape.Core;

namespace Scape.Demo;

/// <summary>
/// The island as the renderer draws it. One tile is one unit, tile y runs along world z, and the
/// ground's top is at y = 0.
/// </summary>
internal static class IslandScene
{
    private const int Across = 2 * TileWorld.RegionSize;
    private const double SeaTop = -0.15;
    private const double WallHeight = 1.4;
    private const double WallHalfThickness = 0.06;
    private const int Sand = 0xD8C690;
    private const int Grass = 0x5A8F3C;

    // Actors take keys above the ground's, a block each, so a box keeps its key from frame to frame.
    private const long ActorKeys = 1_000_000;
    private const long KeysPerActor = 1_000;

    private static readonly (TileFlags Flag, double X, double Z, bool AlongX)[] WallEdges =
    [
        (TileFlags.WallNorth, 0.5, 1, true),
        (TileFlags.WallSouth, 0.5, 0, true),
        (TileFlags.WallWest, 0, 0.5, false),
        (TileFlags.WallEast, 1, 0.5, false),
    ];

    private static readonly Dictionary<int, Part[]> ObjectParts = new()
    {
        [ObjectId.Door] = [new(0, 1.4, 0.3, 0x7A4A24)],
        [ObjectId.StairsUp] = [new(0, 0.5, 0.05, 0x9C8AA8)],
        [ObjectId.StairsDown] = [new(-0.05, 0.02, 0.1, 0x2E2E2E)],
        [ObjectId.Tree] = [new(0, 1.1, 0.38, 0x6B4423), new(0.9, 2.4, 0.02, 0x2E6B2E)],
        [ObjectId.Rock] = [new(0, 0.7, 0.12, 0x7A7F86)],
        [ObjectId.FishingSpot] = [new(SeaTop, SeaTop + 0.06, 0.2, 0x9FE3F0)],
        [ObjectId.BankBooth] = [new(0, 1.2, 0.08, 0xC9A23A)],
        [ObjectId.Anvil] = [new(0, 0.6, 0.25, 0x55595E)],
        [ObjectId.Furnace] = [new(0, 1.8, 0.05, 0x8C3B2A)],
    };

    private static readonly RenderLight Sun = new()
    {
        Key = 1,
        Kind = RenderLightKind.Directional,
        Intensity = 76000,
        Colour = new Vector3(1, 0.95, 0.85),
        Direction = new Vector3(-0.4, -0.7, -0.6).Normalized(),
        SunAngularRadius = 1.2,
    };

    private static readonly Sky Sky = new() { Ambient = 22000, Quality = SkyQuality.Fair };

    /// <summary>
    /// Everything that does not move: the sea, the land with sand beside the water, the walls and
    /// each placed object.
    /// </summary>
    public static IReadOnlyList<RenderObject> Ground(WorldPlan plan)
    {
        var sea = Box(new Vector3(Across / 2.0, SeaTop - 0.5, Across / 2.0), new Vector3(Across * 1.5, 0.5, Across * 1.5), 0x1D4E89);
        IEnumerable<(Matrix4 Transform, int Colour)> boxes = [sea, .. Land(plan.Map), .. Walls(plan.Map), .. Objects(plan)];
        return [.. boxes.Select((box, i) => new RenderObject { Key = i + 1, Transform = box.Transform, Colour = Linear(box.Colour) })];
    }

    /// <summary>The boxes of <paramref name="actor"/> in <paramref name="pose"/>, <paramref name="t"/> of the way through a tick.</summary>
    public static IEnumerable<RenderObject> Posed(Actor actor, Pose pose, double t)
    {
        var at = StandingAt(actor, t);
        var placed = Matrix4.TranslationValues(at.X, at.Y, at.Z).Multiplied(Matrix4.RotationY(actor.Facing));
        return ModelPose.Place(actor.Model, pose).Select((box, i) => new RenderObject
        {
            Key = ActorKeys + ((long)actor.Id * KeysPerActor) + i,
            Transform = placed.Multiplied(box),
            Colour = Linear(actor.Model.Boxes[i].Colour),
        });
    }

    /// <summary>Where <paramref name="actor"/> stands <paramref name="t"/> of the way from its last tile to its tile.</summary>
    public static Vector3 StandingAt(Actor actor, double t) => new(
        actor.From.X + ((actor.Mover.Position.X - actor.From.X) * t) + 0.5,
        0,
        actor.From.Y + ((actor.Mover.Position.Y - actor.From.Y) * t) + 0.5);

    /// <summary>The frame for a view whose width over height is <paramref name="aspect"/>.</summary>
    public static RenderScene Of(IReadOnlyList<RenderObject> objects, OrbitView view, double aspect)
    {
        // The renderer measures the angle across the shorter side. The view's angle is always up the screen.
        var angle = aspect < 1 ? 2 * Math.Atan(Math.Tan(view.FovY / 2) * aspect) : view.FovY;
        return new RenderScene
        {
            Objects = objects,
            Camera = new RenderCamera { Position = view.Position, Target = view.Target, FieldOfView = angle * 180 / Math.PI },
            Lights = [Sun],
            Sky = Sky,
        };
    }

    // Land is drawn in runs of one colour along each row, so the ground is a few hundred boxes.
    private static IEnumerable<(Matrix4, int)> Land(TileMap map)
    {
        for (var y = 0; y < Across; y++)
        {
            var start = 0;
            for (var x = 1; x <= Across; x++)
            {
                var colour = GroundColour(map, start, y);
                if (x < Across && GroundColour(map, x, y) == colour)
                {
                    continue;
                }

                if (colour is { } c)
                {
                    yield return Box(new Vector3((start + x) / 2.0, -0.1, y + 0.5), new Vector3((x - start) / 2.0, 0.1, 0.5), c);
                }

                start = x;
            }
        }
    }

    private static int? GroundColour(TileMap map, int x, int y)
    {
        if (IsWater(map, x, y))
        {
            return null;
        }

        return IsWater(map, x - 1, y) || IsWater(map, x + 1, y) || IsWater(map, x, y - 1) || IsWater(map, x, y + 1) ? Sand : Grass;
    }

    private static bool IsWater(TileMap map, int x, int y) => map.FlagsAt(new TileCoord(x, y, 0)).Has(TileFlags.Water);

    private static IEnumerable<(Matrix4, int)> Walls(TileMap map) =>
        from y in Enumerable.Range(0, Across)
        from x in Enumerable.Range(0, Across)
        from edge in WallEdges
        where map.FlagsAt(new TileCoord(x, y, 0)).Has(edge.Flag)
        select Box(
            new Vector3(x + edge.X, WallHeight / 2, y + edge.Z),
            new Vector3(edge.AlongX ? 0.5 : WallHalfThickness, WallHeight / 2, edge.AlongX ? WallHalfThickness : 0.5),
            0xD9CFB8);

    private static IEnumerable<(Matrix4, int)> Objects(WorldPlan plan) =>
        from placement in plan.Objects
        where placement.Tile.Plane == 0
        from tile in DefaultObjects.All.Require(placement.ObjectId).FootprintAt(placement.Tile).Tiles
        from part in ObjectParts[placement.ObjectId]
        select Box(
            new Vector3(tile.X + 0.5, (part.Bottom + part.Top) / 2, tile.Y + 0.5),
            new Vector3(0.5 - part.Inset, (part.Top - part.Bottom) / 2, 0.5 - part.Inset),
            part.Colour);

    // The renderer's cube is 2 units across, so a box's scale is half its size.
    private static (Matrix4, int) Box(Vector3 centre, Vector3 halfSize, int colour)
    {
        var transform = Matrix4.Identity();
        transform.SetTranslation(centre);
        transform.ScaleByDouble(halfSize.X, halfSize.Y, halfSize.Z, 1);
        return (transform, colour);
    }

    // Model colours are sRGB as 0xRRGGBB, and the renderer takes linear.
    private static Vector3 Linear(int rgb) => new(Channel(rgb >> 16), Channel(rgb >> 8), Channel(rgb));

    private static double Channel(int value)
    {
        var c = (value & 0xFF) / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>One box of a placed object, by its height above the ground and how far it sits in from the tile's edges.</summary>
    private sealed record Part(double Bottom, double Top, double Inset, int Colour);
}
