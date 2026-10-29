namespace Scape.Core.Tests;

internal static class WorldSupport
{
    /// <summary>The first tile on both axes of the window a random map opens.</summary>
    public const int Low = 20;

    /// <summary>Tiles across and down the window.</summary>
    public const int Size = 12;

    public static TileCoord At(int x, int y) => new(x, y, 0);

    /// <summary>Four open regions, tiles 0 to 127 on both axes of plane 0.</summary>
    public static TileMap OpenField()
    {
        var map = new TileMap();
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        return map;
    }

    /// <summary>
    /// A map whose only open ground is the window, with <paramref name="flags"/>
    /// for its tiles row by row from the south-west corner. The rest of the
    /// region round it is blocked.
    /// </summary>
    public static TileMap WindowMap(IReadOnlyList<TileFlags> flags)
    {
        var region = FlagRegion.Open(new RegionKey(0, 0, 0));
        for (var i = 0; i < TileWorld.RegionSize * TileWorld.RegionSize; i++)
        {
            region.SetLocal(i, TileFlags.Blocked);
        }

        var map = new TileMap();
        map.AddRegion(region);
        for (var i = 0; i < flags.Count; i++)
        {
            map.SetFlags(At(Low + i % Size, Low + i / Size), flags[i]);
        }

        return map;
    }
}
