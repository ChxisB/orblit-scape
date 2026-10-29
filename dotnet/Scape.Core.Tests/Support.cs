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

internal static class ItemSupport
{
    public const int Coins = 1;
    public const int Logs = 2;
    public const int Sword = 3;
    public const int Helmet = 4;
    public const int Arrows = 5;

    /// <summary>One skill, fighting, capped at 50.</summary>
    public static readonly SkillSet Skills = new(new XpCurve(30, 4), [new SkillDef("fighting", "Fighting", 50)]);

    public static ItemSet Items() => new(
        [
            new ItemDef(Coins, "Coins", stackable: true, value: 1),
            new ItemDef(Logs, "Logs", value: 4),
            new ItemDef(Sword, "Sword", equipSlot: "weapon", needs: new Needs("fighting", 5)),
            new ItemDef(Helmet, "Helmet", equipSlot: "head"),
            new ItemDef(Arrows, "Arrows", stackable: true, equipSlot: "weapon"),
        ],
        Skills);
}
