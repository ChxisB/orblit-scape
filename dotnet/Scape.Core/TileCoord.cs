namespace Scape.Core;

/// <summary>The size of the world, in tiles, regions and planes.</summary>
public static class TileWorld
{
    /// <summary>Tiles across and down one region.</summary>
    public const int RegionSize = 64;

    /// <summary>Tiles from one edge of the world to the other, on both axes: 256 regions.</summary>
    public const int WorldSize = 256 * RegionSize;

    /// <summary>Floors that can stack on top of each other.</summary>
    public const int PlaneCount = 4;

    /// <summary>Regions from one edge of the world to the other.</summary>
    public const int RegionsAcross = WorldSize / RegionSize;
}

/// <summary>
/// Which 64 by 64 square of one plane a tile is in.
/// </summary>
/// <remarks>
/// Region (0, 0) holds tiles 0 to 63 on both axes. A terrain whose regions are
/// 64 texels across with one texel to a tile cuts the ground the same way, with
/// <see cref="Y"/> as its z.
/// </remarks>
public readonly record struct RegionKey
{
    public RegionKey(int x, int y, int plane)
    {
        if (x < 0 || x >= TileWorld.RegionsAcross || y < 0 || y >= TileWorld.RegionsAcross)
        {
            throw new ArgumentException(FormattableString.Invariant($"Region ({x}, {y}) is outside the world."));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(plane);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, TileWorld.PlaneCount);
        X = x;
        Y = y;
        Plane = plane;
    }

    public int X { get; }

    public int Y { get; }

    public int Plane { get; }

    public override string ToString() => FormattableString.Invariant($"RegionKey({X}, {Y}, {Plane})");
}

/// <summary>One tile of the world. <see cref="Y"/> grows to the north.</summary>
public readonly record struct TileCoord
{
    public TileCoord(int x, int y, int plane)
    {
        if (x < 0 || x >= TileWorld.WorldSize || y < 0 || y >= TileWorld.WorldSize)
        {
            throw new ArgumentException(FormattableString.Invariant($"Tile ({x}, {y}) is outside the world."));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(plane);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, TileWorld.PlaneCount);
        X = x;
        Y = y;
        Plane = plane;
    }

    public int X { get; }

    public int Y { get; }

    public int Plane { get; }

    public RegionKey Region => new(X / TileWorld.RegionSize, Y / TileWorld.RegionSize, Plane);

    /// <summary>The place of this tile in its region's flag list, row by row.</summary>
    public int LocalIndex => Y % TileWorld.RegionSize * TileWorld.RegionSize + X % TileWorld.RegionSize;

    public override string ToString() => FormattableString.Invariant($"TileCoord({X}, {Y}, {Plane})");
}
