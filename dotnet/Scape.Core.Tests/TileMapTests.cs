using System.Diagnostics;
using static Scape.Core.Tests.WorldSupport;

namespace Scape.Core.Tests;

public class TileMapTests(ITestOutputHelper output)
{
    // Tile coordinates.

    [Fact]
    public void RefusesAPlaneXOrYOutsideTheWorld()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(-1, 0, 0));
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(0, -1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(TileWorld.WorldSize, 0, 0));
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(0, TileWorld.WorldSize, 0));
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(0, 0, TileWorld.PlaneCount));
        Assert.ThrowsAny<ArgumentException>(() => new TileCoord(0, 0, -1));
    }

    [Fact]
    public void ComparesByValue()
    {
        Assert.Equal(new TileCoord(3, 4, 1), new TileCoord(3, 4, 1));
        Assert.Equal(new TileCoord(3, 4, 1).GetHashCode(), new TileCoord(3, 4, 1).GetHashCode());
        Assert.NotEqual(new TileCoord(3, 4, 1), new TileCoord(3, 4, 2));
    }

    [Fact]
    public void NamesTheRegionThatHoldsIt()
    {
        Assert.Equal(new RegionKey(0, 0, 2), new TileCoord(63, 63, 2).Region);
        Assert.Equal(new RegionKey(1, 0, 0), new TileCoord(64, 0, 0).Region);
        Assert.Equal(new RegionKey(2, 3, 3), new TileCoord(130, 200, 3).Region);
    }

    // Stepping.

    [Fact]
    public void AllowsAStepInEachOfTheEightDirectionsOnOpenGround()
    {
        var map = OpenField();
        var centre = At(10, 10);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                Assert.Equal(dx != 0 || dy != 0, map.CanStep(centre, At(10 + dx, 10 + dy)));
            }
        }
    }

    [Fact]
    public void RefusesNoStepALongStepAndAStepBetweenPlanes()
    {
        var map = OpenField();
        Assert.False(map.CanStep(At(5, 5), At(5, 5)));
        Assert.False(map.CanStep(At(5, 5), At(7, 5)));
        Assert.False(map.CanStep(At(5, 5), new TileCoord(5, 6, 1)));
    }

    [Fact]
    public void RefusesAStepIntoABlockedTile()
    {
        var map = OpenField();
        map.SetFlags(At(11, 10), TileFlags.Blocked);
        Assert.False(map.CanStep(At(10, 10), At(11, 10)));
        Assert.True(map.CanStep(At(11, 10), At(10, 10)));
    }

    [Fact]
    public void RefusesAStepIntoWater()
    {
        var map = OpenField();
        map.SetFlags(At(11, 10), TileFlags.Water);
        Assert.False(map.CanStep(At(10, 10), At(11, 10)));
    }

    [Fact]
    public void LetsAWallBlockAStepFromBothSides()
    {
        var map = OpenField();
        map.SetFlags(At(10, 10), TileFlags.WallNorth);
        Assert.False(map.CanStep(At(10, 10), At(10, 11)));
        Assert.False(map.CanStep(At(10, 11), At(10, 10)));
        Assert.True(map.CanStep(At(10, 10), At(11, 10)));
    }

    [Fact]
    public void ReadsAWallFlaggedOnTheOtherTileOfTheEdgeToo()
    {
        var map = OpenField();
        map.SetFlags(At(10, 11), TileFlags.WallSouth);
        Assert.False(map.CanStep(At(10, 10), At(10, 11)));
        Assert.False(map.CanStep(At(10, 11), At(10, 10)));
    }

    [Fact]
    public void KnowsEachWallSide()
    {
        (TileFlags Flag, int Dx, int Dy)[] sides =
            [(TileFlags.WallNorth, 0, 1), (TileFlags.WallEast, 1, 0), (TileFlags.WallSouth, 0, -1), (TileFlags.WallWest, -1, 0)];
        foreach (var (flag, dx, dy) in sides)
        {
            var map = OpenField();
            map.SetFlags(At(10, 10), flag);
            Assert.False(map.CanStep(At(10, 10), At(10 + dx, 10 + dy)));
            Assert.True(map.CanStep(At(10, 10), At(10 - dx, 10 - dy)));
        }
    }

    [Fact]
    public void RefusesADiagonalPastABlockedCorner()
    {
        var map = OpenField();
        map.SetFlags(At(11, 10), TileFlags.Blocked);
        Assert.False(map.CanStep(At(10, 10), At(11, 11)));
        Assert.False(map.CanStep(At(11, 11), At(10, 10)));
        Assert.True(map.CanStep(At(10, 10), At(9, 11)));
    }

    [Fact]
    public void RefusesADiagonalAcrossAWallOnEitherEdge()
    {
        var map = OpenField();
        map.SetFlags(At(10, 10), TileFlags.WallEast);
        Assert.False(map.CanStep(At(10, 10), At(11, 11)));
        var other = OpenField();
        other.SetFlags(At(11, 10), TileFlags.WallNorth);
        Assert.False(other.CanStep(At(10, 10), At(11, 11)));
    }

    [Fact]
    public void KeepsATileOnlyAnNpcMayNotEnterOpenToAPlayer()
    {
        var map = OpenField();
        map.SetFlags(At(11, 10), TileFlags.NoNpc);
        Assert.True(map.CanStep(At(10, 10), At(11, 10)));
        Assert.False(map.CanStep(At(10, 10), At(11, 10), Walker.Npc));
    }

    [Fact]
    public void TreatsATileInARegionThatIsNotLoadedAsBlocked()
    {
        var map = new TileMap();
        map.AddRegion(FlagRegion.Open(new RegionKey(0, 0, 0)));
        Assert.False(map.CanStep(At(63, 10), At(64, 10)));
        Assert.Equal(TileFlags.Blocked, map.FlagsAt(At(200, 200)));
    }

    // Setting flags.

    [Fact]
    public void SetFlagsRefusesATileInARegionThatIsNotLoaded() =>
        Assert.Throws<InvalidOperationException>(() => new TileMap().SetFlags(At(0, 0), TileFlags.Blocked));

    [Fact]
    public void CombinesFlags()
    {
        var map = OpenField();
        map.SetFlags(At(1, 1), TileFlags.Blocked | TileFlags.Water);
        Assert.True(map.FlagsAt(At(1, 1)).Has(TileFlags.Blocked));
        Assert.True(map.FlagsAt(At(1, 1)).Has(TileFlags.Water));
        Assert.False(map.FlagsAt(At(1, 1)).Has(TileFlags.NoNpc));
    }

    [Fact]
    public void BuildsA16By16RegionWorld()
    {
        var watch = Stopwatch.StartNew();
        var map = new TileMap();
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        watch.Stop();

        // The figure is recorded and not judged.
        output.WriteLine($"16x16 regions ({16 * 16 * 64 * 64} tiles): {watch.Elapsed.TotalMicroseconds:F0} us");
        Assert.Equal(256, map.RegionCount);
    }
}
