namespace Scape.Core;

/// <summary>Who is stepping. Creatures keep out of tiles flagged <see cref="TileFlags.NoNpc"/>.</summary>
public enum Walker
{
    Player,
    Npc,
}

/// <summary>The flags of every loaded region, and the rules for one step over them.</summary>
public sealed class TileMap
{
    private readonly Dictionary<RegionKey, FlagRegion> _regions = [];

    public int RegionCount => _regions.Count;

    /// <summary>Loads <paramref name="region"/>, replacing one with the same key.</summary>
    public void AddRegion(FlagRegion region) => _regions[region.Key] = region;

    public FlagRegion? RegionAt(RegionKey key) => _regions.GetValueOrDefault(key);

    /// <summary>
    /// The flags on <paramref name="tile"/>, or <see cref="TileFlags.Blocked"/> when its region
    /// is not loaded, so nobody walks off the edge of what is known.
    /// </summary>
    public TileFlags FlagsAt(TileCoord tile) =>
        _regions.TryGetValue(tile.Region, out var region) ? region.FlagsAtLocal(tile.LocalIndex) : TileFlags.Blocked;

    public void SetFlags(TileCoord tile, TileFlags flags)
    {
        if (!_regions.TryGetValue(tile.Region, out var region))
        {
            throw new InvalidOperationException($"{tile.Region} is not loaded.");
        }

        region.SetLocal(tile.LocalIndex, flags);
    }

    /// <summary>
    /// Whether a <paramref name="walker"/> may step from <paramref name="from"/> to the tile
    /// beside it, <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// A diagonal needs both tiles at its corners open and no wall on any of the four edges
    /// round them. A step to the same tile, past the next tile or to another plane is never
    /// allowed.
    /// </remarks>
    public bool CanStep(TileCoord from, TileCoord to, Walker walker = Walker.Player)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (from.Plane != to.Plane || Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
        {
            return false;
        }

        return dx == 0 || dy == 0 ? CanStepStraight(from, to, walker) : CanStepDiagonal(from, to, walker);
    }

    /// <summary>
    /// Whether no wall stands on the edge between <paramref name="a"/> and <paramref name="b"/>,
    /// which must be beside each other in a straight line. A wall counts from either tile.
    /// </summary>
    public bool EdgeOpen(TileCoord a, TileCoord b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return !FlagsAt(a).Has(WallToward(dx, dy)) && !FlagsAt(b).Has(WallToward(-dx, -dy));
    }

    private bool CanStepDiagonal(TileCoord from, TileCoord to, Walker walker)
    {
        var alongX = new TileCoord(to.X, from.Y, from.Plane);
        var alongY = new TileCoord(from.X, to.Y, from.Plane);
        return CanStepStraight(from, alongX, walker)
            && CanStepStraight(alongX, to, walker)
            && CanStepStraight(from, alongY, walker)
            && CanStepStraight(alongY, to, walker);
    }

    private bool CanStepStraight(TileCoord from, TileCoord to, Walker walker)
    {
        var entering = FlagsAt(to);
        if (entering.Has(TileFlags.Blocked | TileFlags.Water))
        {
            return false;
        }

        if (walker == Walker.Npc && entering.Has(TileFlags.NoNpc))
        {
            return false;
        }

        return EdgeOpen(from, to);
    }

    private static TileFlags WallToward(int dx, int dy)
    {
        if (dy > 0)
        {
            return TileFlags.WallNorth;
        }

        if (dy < 0)
        {
            return TileFlags.WallSouth;
        }

        return dx > 0 ? TileFlags.WallEast : TileFlags.WallWest;
    }
}
