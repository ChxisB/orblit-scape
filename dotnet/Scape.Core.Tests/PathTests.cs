using static Scape.Core.Tests.WorldSupport;

namespace Scape.Core.Tests;

public class PathTests
{
    private static readonly TileFlags[] Walls = [TileFlags.WallNorth, TileFlags.WallEast, TileFlags.WallSouth, TileFlags.WallWest];

    private static readonly Footprint Target = new(At(30, 30), 3, 3);

    private static TileMap RandomWindow(Random random) =>
        WindowMap([.. Enumerable.Range(0, Size * Size).Select(_ => RandomFlags(random))]);

    private static TileFlags RandomFlags(Random random)
    {
        var flags = TileFlags.Open;
        if (random.Next(100) < 22)
        {
            flags |= TileFlags.Blocked;
        }

        if (random.Next(100) < 5)
        {
            flags |= TileFlags.Water;
        }

        foreach (var wall in Walls.Where(_ => random.Next(100) < 6))
        {
            flags |= wall;
        }

        return flags;
    }

    private static TileCoord InWindow(Random random) => At(Low + random.Next(Size), Low + random.Next(Size));

    /// <summary>
    /// Steps to every tile of the window, by sweeping until nothing improves.
    /// It shares no code with the search it checks.
    /// </summary>
    private static Dictionary<TileCoord, int> BruteForce(TileMap map, TileCoord start)
    {
        var distance = new Dictionary<TileCoord, int> { [start] = 0 };
        var changed = true;
        while (changed)
        {
            var improved = distance
                .SelectMany(entry => Neighbours(entry.Key)
                    .Where(next => map.CanStep(entry.Key, next))
                    .Select(next => (Tile: next, Steps: entry.Value + 1)))
                .Where(step => step.Steps < distance.GetValueOrDefault(step.Tile, int.MaxValue))
                .ToList();
            foreach (var (tile, steps) in improved)
            {
                distance[tile] = Math.Min(steps, distance.GetValueOrDefault(tile, int.MaxValue));
            }

            changed = improved.Count > 0;
        }

        return distance;
    }

    private static IEnumerable<TileCoord> Neighbours(TileCoord tile) =>
        from dy in new[] { -1, 0, 1 }
        from dx in new[] { -1, 0, 1 }
        let x = tile.X + dx
        let y = tile.Y + dy
        where x >= Low && y >= Low && x < Low + Size && y < Low + Size
        select At(x, y);

    private static int SquaredDistance(TileCoord a, TileCoord b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static void AssertWalkable(TileMap map, TileCoord from, IEnumerable<TileCoord> steps)
    {
        var here = from;
        foreach (var step in steps)
        {
            Assert.True(map.CanStep(here, step), $"{here} to {step}");
            here = step;
        }
    }

    // Finding a path.

    [Fact]
    public void IsTheShortestPathOnRandomSmallMaps()
    {
        var random = new Random(7);
        for (var round = 0; round < 150; round++)
        {
            var map = RandomWindow(random);
            var from = InWindow(random);
            var to = InWindow(random);
            if (map.FlagsAt(from).Has(TileFlags.Blocked))
            {
                continue;
            }

            var expected = BruteForce(map, from);
            var result = PathSearch.Find(map, from, to);

            AssertWalkable(map, from, result.Steps);
            if (from == to)
            {
                Assert.True(result.Reached);
                Assert.Empty(result.Steps);
            }
            else if (expected.TryGetValue(to, out var steps))
            {
                Assert.True(result.Reached, $"round {round}");
                Assert.Equal(steps, result.Steps.Count);
                Assert.Equal(to, result.Steps[^1]);
            }
            else
            {
                Assert.False(result.Reached, $"round {round}");
            }
        }
    }

    [Fact]
    public void EndsOnTheNearestReachableTileWhenTheTargetIsNot()
    {
        var random = new Random(11);
        var count = 0;
        for (var round = 0; round < 150 && count < 40; round++)
        {
            var map = RandomWindow(random);
            var from = InWindow(random);
            var to = InWindow(random);
            var reachable = map.FlagsAt(from).Has(TileFlags.Blocked) ? null : BruteForce(map, from);
            if (reachable is null || reachable.ContainsKey(to))
            {
                continue;
            }

            var result = PathSearch.Find(map, from, to);
            var end = result.Steps.Count == 0 ? from : result.Steps[^1];

            Assert.False(result.Reached);
            Assert.True(reachable.ContainsKey(end));
            Assert.Equal(reachable.Keys.Min(tile => SquaredDistance(tile, to)), SquaredDistance(end, to));
            count++;
        }

        Assert.True(count > 10, $"only {count} rounds had a target out of reach");
    }

    [Fact]
    public void GoesRoundAWallByTheShortWay()
    {
        var map = OpenField();
        for (var y = 10; y < 20; y++)
        {
            map.SetFlags(At(15, y), TileFlags.Blocked);
        }

        var result = PathSearch.Find(map, At(12, 15), At(18, 15));

        Assert.True(result.Reached);
        AssertWalkable(map, At(12, 15), result.Steps);
        Assert.Equal(12, result.Steps.Count);
    }

    [Fact]
    public void RefusesATargetOnAnotherPlane() =>
        Assert.Throws<ArgumentException>(() => PathSearch.Find(OpenField(), At(1, 1), new TileCoord(1, 2, 1)));

    [Fact]
    public void StaysInsideTheBoxRoundTheTwoPoints()
    {
        var map = OpenField();
        for (var y = 0; y < 40; y++)
        {
            map.SetFlags(At(15, y), TileFlags.Blocked);
        }

        Assert.False(PathSearch.Find(map, At(12, 5), At(18, 5), margin: 2).Reached);
    }

    // The mover.

    [Fact]
    public void WalksATileATickAndRunsTwo()
    {
        var map = OpenField();
        TileCoord[] path = [At(1, 0), At(2, 0), At(3, 0), At(4, 0)];

        var walker = new Mover(At(0, 0));
        walker.SetPath(path);
        walker.Advance(map);
        Assert.Equal(At(1, 0), walker.Position);
        Assert.Equal(new[] { At(1, 0) }, walker.StepsLastTick);

        var runner = new Mover(At(0, 0)) { Speed = MoveSpeed.Run };
        runner.SetPath(path);
        runner.Advance(map);
        Assert.Equal(At(2, 0), runner.Position);
        Assert.Equal(new[] { At(1, 0), At(2, 0) }, runner.StepsLastTick);
    }

    [Fact]
    public void StopsCleanlyOnAnOddLengthAtARun()
    {
        var map = OpenField();
        var runner = new Mover(At(0, 0)) { Speed = MoveSpeed.Run };
        runner.SetPath([At(1, 0), At(2, 0), At(3, 0)]);

        runner.Advance(map);
        Assert.True(runner.IsMoving);
        runner.Advance(map);

        Assert.Equal(At(3, 0), runner.Position);
        Assert.Equal(new[] { At(3, 0) }, runner.StepsLastTick);
        Assert.False(runner.IsMoving);
        runner.Advance(map);
        Assert.Empty(runner.StepsLastTick);
    }

    [Fact]
    public void StopsWhenTheWorldChangedUnderThePath()
    {
        var map = OpenField();
        var mover = new Mover(At(0, 0));
        mover.SetPath([At(1, 0), At(2, 0), At(3, 0)]);
        mover.Advance(map);
        map.SetFlags(At(2, 0), TileFlags.Blocked);

        mover.Advance(map);

        Assert.Equal(At(1, 0), mover.Position);
        Assert.False(mover.IsMoving);
    }

    [Fact]
    public void RefusesAPathThatDoesNotStartBesideIt()
    {
        var mover = new Mover(At(0, 0));
        Assert.Throws<ArgumentException>(() => mover.SetPath([At(2, 0)]));
        Assert.Throws<ArgumentException>(() => mover.SetPath([At(1, 0), At(3, 0)]));
    }

    [Fact]
    public void ClearPathStopsIt()
    {
        var mover = new Mover(At(0, 0));
        mover.SetPath([At(1, 0), At(2, 0)]);
        mover.ClearPath();
        Assert.False(mover.IsMoving);
    }

    [Fact]
    public void WalksAFoundPathToItsEnd()
    {
        var map = OpenField();
        var mover = new Mover(At(5, 5));
        mover.SetPath(PathSearch.Find(map, mover.Position, At(9, 8)).Steps);
        var ticks = 0;
        while (mover.IsMoving)
        {
            mover.Advance(map);
            ticks++;
        }

        Assert.Equal(At(9, 8), mover.Position);
        Assert.Equal(4, ticks);
    }

    // Reach.

    private static TileMap FieldWithTarget()
    {
        var map = OpenField();
        foreach (var tile in Target.Tiles)
        {
            map.SetFlags(tile, TileFlags.Blocked);
        }

        return map;
    }

    private static bool Touches(TileCoord a, TileCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    [Fact]
    public void AMoverStopsNextToAThreeByThreeObjectOnEverySide()
    {
        var map = FieldWithTarget();
        foreach (var from in new[] { At(31, 40), At(31, 20), At(40, 31), At(20, 31) })
        {
            var result = PathSearch.FindToReach(map, from, new Beside(Target));
            Assert.True(result.Reached, $"{from}");
            var end = result.Steps[^1];
            Assert.Contains(Target.Tiles, tile => Touches(tile, end));
            Assert.DoesNotContain(end, Target.Tiles);
        }
    }

    [Fact]
    public void TheNearestSideIsTheOneItStopsAt()
    {
        var result = PathSearch.FindToReach(FieldWithTarget(), At(31, 40), new Beside(Target));
        Assert.Equal(33, result.Steps[^1].Y);
        Assert.Equal(7, result.Steps.Count);
    }

    [Fact]
    public void ACornerTileIsNotBesideTheObject()
    {
        var map = FieldWithTarget();
        Assert.False(new Beside(Target).IsSatisfied(map, At(29, 29)));
        Assert.True(new Beside(Target).IsSatisfied(map, At(29, 31)));
    }

    [Fact]
    public void AWallBetweenTheTileAndTheObjectDoesNotCount()
    {
        var map = FieldWithTarget();
        map.SetFlags(At(29, 31), TileFlags.WallEast);
        Assert.False(new Beside(Target).IsSatisfied(map, At(29, 31)));

        var result = PathSearch.FindToReach(map, At(20, 31), new Beside(Target));
        Assert.NotEqual(At(29, 31), result.Steps[^1]);
        Assert.True(new Beside(Target).IsSatisfied(map, result.Steps[^1]));
    }

    [Fact]
    public void StandingOnATileIsReachedOnlyThere()
    {
        var map = OpenField();
        var result = PathSearch.FindToReach(map, At(5, 5), new OnTile(At(8, 9)));
        Assert.Equal(At(8, 9), result.Steps[^1]);
        Assert.False(new OnTile(At(8, 9)).IsSatisfied(map, At(8, 8)));
    }

    [Fact]
    public void ARangeReachesFromAfarWithAClearLine()
    {
        var map = FieldWithTarget();
        var reach = new Within(Target, 5);
        Assert.True(reach.IsSatisfied(map, At(36, 31)));
        Assert.False(reach.IsSatisfied(map, At(38, 31)));
        map.SetFlags(At(34, 31), TileFlags.WallEast);
        Assert.False(reach.IsSatisfied(map, At(36, 31)));
    }

    [Fact]
    public void ARangeFindsATileInLineWhenTheFirstOnesAreNot()
    {
        var map = FieldWithTarget();
        for (var y = 25; y < 40; y++)
        {
            map.SetFlags(At(36, y), TileFlags.Blocked);
        }

        var reach = new Within(Target, 5);
        var result = PathSearch.FindToReach(map, At(40, 31), reach);

        Assert.True(result.Reached);
        Assert.True(reach.IsSatisfied(map, result.Steps[^1]));
    }

    // Line of sight.

    [Fact]
    public void IsClearOnOpenGroundAndBetweenATileAndItself()
    {
        var map = OpenField();
        Assert.True(LineOfSight.HasClearLine(map, At(5, 5), At(12, 9)));
        Assert.True(LineOfSight.HasClearLine(map, At(5, 5), At(5, 5)));
    }

    [Fact]
    public void StopsAtABlockedTileInTheWayButNotOnTheLastOne()
    {
        var map = OpenField();
        map.SetFlags(At(8, 5), TileFlags.Blocked);
        Assert.False(LineOfSight.HasClearLine(map, At(5, 5), At(12, 5)));
        Assert.True(LineOfSight.HasClearLine(map, At(5, 5), At(8, 5)));
    }

    [Fact]
    public void StopsAtAWall()
    {
        var map = OpenField();
        map.SetFlags(At(8, 5), TileFlags.WallEast);
        Assert.False(LineOfSight.HasClearLine(map, At(5, 5), At(12, 5)));
        Assert.False(LineOfSight.HasClearLine(map, At(12, 5), At(5, 5)));
    }
}
