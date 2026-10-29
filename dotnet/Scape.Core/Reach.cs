namespace Scape.Core;

/// <summary>A rectangle of tiles on one plane, such as a tree or a door frame, named by its south-west tile.</summary>
public sealed class Footprint
{
    public Footprint(TileCoord origin, int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentException("A footprint is at least one tile each way.");
        }

        if (origin.X + width > TileWorld.WorldSize || origin.Y + height > TileWorld.WorldSize)
        {
            throw new ArgumentException("The footprint runs off the world.");
        }

        Origin = origin;
        Width = width;
        Height = height;
    }

    public TileCoord Origin { get; }

    public int Width { get; }

    public int Height { get; }

    public IEnumerable<TileCoord> Tiles
    {
        get
        {
            for (var y = Origin.Y; y < Origin.Y + Height; y++)
            {
                for (var x = Origin.X; x < Origin.X + Width; x++)
                {
                    yield return new TileCoord(x, y, Origin.Plane);
                }
            }
        }
    }

    /// <summary>One tile.</summary>
    public static Footprint OfTile(TileCoord tile) => new(tile, 1, 1);

    /// <summary>The tile of the footprint nearest <paramref name="from"/>, on the footprint's plane.</summary>
    public TileCoord ClosestTile(TileCoord from) => new(
        Math.Clamp(from.X, Origin.X, Origin.X + Width - 1),
        Math.Clamp(from.Y, Origin.Y, Origin.Y + Height - 1),
        Origin.Plane);

    /// <summary>The squared distance in tiles from <paramref name="from"/> to <see cref="ClosestTile"/>.</summary>
    public int DistanceSquared(TileCoord from)
    {
        var near = ClosestTile(from);
        var dx = from.X - near.X;
        var dy = from.Y - near.Y;
        return dx * dx + dy * dy;
    }
}

/// <summary>Where a mover has to stand to do something. A small value, not a flag.</summary>
public abstract class Reach
{
    private protected Reach(Footprint target) => Target = target;

    public Footprint Target { get; }

    /// <summary>Whether standing on <paramref name="at"/> satisfies this reach.</summary>
    public bool IsSatisfied(TileMap map, TileCoord at)
    {
        if (at.Plane != Target.Origin.Plane)
        {
            return false;
        }

        var near = Target.ClosestTile(at);
        return this switch
        {
            OnTile => at == near,
            Beside => SharesEdge(map, at, near),
            Within within => Chebyshev(at, near) <= within.Range && LineOfSight.HasClearLine(map, at, near),
            _ => throw new InvalidOperationException($"{GetType().Name} is not a reach."),
        };
    }

    private static bool SharesEdge(TileMap map, TileCoord at, TileCoord near) =>
        Math.Abs(at.X - near.X) + Math.Abs(at.Y - near.Y) == 1 && map.EdgeOpen(at, near);

    private static int Chebyshev(TileCoord a, TileCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>On one tile.</summary>
public sealed class OnTile(TileCoord tile) : Reach(Footprint.OfTile(tile));

/// <summary>On a tile that shares an edge with the target, with no wall between. A tile at a corner does not count.</summary>
public sealed class Beside(Footprint target) : Reach(target);

/// <summary>Within <see cref="Range"/> tiles of the target, with a clear line to it.</summary>
public sealed class Within : Reach
{
    public Within(Footprint target, int range)
        : base(target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(range, 1);
        Range = range;
    }

    public int Range { get; }
}
