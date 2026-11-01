using Orblit.Terrain;
using Orblit.VectorMath;
using Scape.Core;
using GroundKey = Orblit.Terrain.RegionKey;

namespace Scape.Client.Tests;

public class TilePickTests
{
    private const double Width = 800;
    private const double Height = 600;

    private readonly Terrain _slope = Slope();

    [Fact]
    public void EveryTileOnASlopeIsPickedByThePixelItIsDrawnAt()
    {
        var view = new OrbitView { Target = TilePick.StandingPoint(_slope, 30, 30), Yaw = 0.9, Pitch = 0.8, Distance = 20 };

        foreach (var (x, y) in new[] { (30, 30), (28, 33), (35, 27), (22, 22), (40, 36) })
        {
            var (px, py) = PixelOf(view, TilePick.StandingPoint(_slope, x, y));
            var ray = view.RayThrough(px, py, Width, Height);

            Assert.Equal(new TileCoord(x, y, 0), TilePick.Pick(_slope, ray));
        }
    }

    [Fact]
    public void ARayThatRisesAboveTheHorizonPicksNothing()
    {
        var view = new OrbitView { Target = TilePick.StandingPoint(_slope, 30, 30), Pitch = 0 };
        Assert.Null(TilePick.Pick(_slope, view.RayThrough(400, 0, Width, Height)));
    }

    [Fact]
    public void GroundOutsideTheWorldPicksNothing()
    {
        var terrain = new Terrain(regionSize: 64);
        terrain.FillHeights(new GroundKey(-1, 0), (_, _) => 0);
        var view = new OrbitView { Target = new Vector3(-10, 0, 10), Pitch = 1.2, Distance = 10 };
        Assert.Null(TilePick.Pick(terrain, view.RayThrough(400, 300, Width, Height)));
    }

    [Fact]
    public void TheTileKeepsThePlaneItIsAskedFor()
    {
        var view = new OrbitView { Target = TilePick.StandingPoint(_slope, 30, 30) };
        var ray = view.RayThrough(400, 300, Width, Height);
        Assert.Equal(2, TilePick.Pick(_slope, ray, plane: 2)!.Value.Plane);
    }

    [Fact]
    public void AnActorStandsInTheMiddleOfItsTileOnTheGround()
    {
        var point = TilePick.StandingPoint(_slope, 10, 12);
        Assert.Equal(10.5, point.X);
        Assert.Equal(12.5, point.Z);
        Assert.Equal(_slope.HeightAt(10.5, 12.5)!.Value, point.Y, 1e-12);
    }

    [Fact]
    public void APointNearTheEdgeOfATileIsPickedAsThatTile()
    {
        var view = new OrbitView { Target = TilePick.StandingPoint(_slope, 30, 30), Yaw = 0.9, Pitch = 0.8, Distance = 20 };
        var (px, py) = PixelOf(view, new Vector3(30.8, _slope.HeightAt(30.8, 30.2)!.Value, 30.2));

        Assert.Equal(new TileCoord(30, 30, 0), TilePick.Pick(_slope, view.RayThrough(px, py, Width, Height)));
    }

    [Fact]
    public void GroundPastTheFarEdgeOfTheWorldPicksNothing()
    {
        var terrain = new Terrain(regionSize: 64);
        terrain.FillHeights(new GroundKey(TileWorld.WorldSize / 64, 0), (_, _) => 0);
        var view = new OrbitView { Target = new Vector3(TileWorld.WorldSize + 10, 0, 10), Pitch = 1.2, Distance = 10 };
        Assert.Null(TilePick.Pick(terrain, view.RayThrough(400, 300, Width, Height)));
    }

    [Fact]
    public void AnActorOffTheGroundStandsAtHeightZero()
    {
        Assert.Equal(0, TilePick.StandingPoint(new Terrain(regionSize: 64), 3, 4).Y);
    }

    private static Terrain Slope()
    {
        var terrain = new Terrain(regionSize: 64);
        terrain.FillHeights(new GroundKey(0, 0), (x, _) => x * 0.5);
        return terrain;
    }

    /// <summary>The pixel a world point is drawn at: the inverse of <see cref="OrbitView.RayThrough"/>.</summary>
    private static (double X, double Y) PixelOf(OrbitView view, Vector3 point)
    {
        var look = view.Forward;
        var right = look.Cross(new Vector3(0, 1, 0)).Normalized();
        var up = right.Cross(look);
        var offset = point - view.Position;
        var depth = offset.Dot(look);
        var reach = depth * Math.Tan(view.FovY / 2);
        var across = offset.Dot(right) / (reach * Width / Height);
        var down = offset.Dot(up) / reach;
        return ((across + 1) / 2 * Width, (1 - down) / 2 * Height);
    }
}
