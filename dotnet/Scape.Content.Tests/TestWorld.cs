using Scape.Core;

namespace Scape.Content.Tests;

/// <summary>
/// A small map with one of everything the default content places, for the tests that need ground to
/// stand on. It ships with no game. Each call gives a new map, since the server changes the flags of
/// the one it is given.
/// </summary>
internal static class TestWorld
{
    // Sea fills the rows below this, so a fishing spot on the first row of land stands beside water.
    private const int Shore = 8;

    public static readonly TileCoord Sea = At(1, 1);
    public static readonly TileCoord Spawn = At(32, 16);

    /// <summary>Open ground beside the spawn tile with nothing on it.</summary>
    public static readonly TileCoord Clear = At(34, 16);

    public static readonly TileCoord ShopDoor = At(22, 24);
    public static readonly TileCoord Shopkeeper = At(22, 27);
    public static readonly TileCoord BankDoor = At(38, 24);
    public static readonly TileCoord[] BankBooths = [At(37, 28), At(39, 28)];
    public static readonly TileCoord Furnace = At(10, 20);
    public static readonly TileCoord StairsUp = At(33, 43);
    public static readonly TileCoord StairsDown = At(33, 43, 1);

    private static readonly ObjectPlacement[] DoorsAndStations =
    [
        new(ObjectId.Door, ShopDoor),
        new(ObjectId.Door, BankDoor),
        new(ObjectId.Door, At(32, 40)),
        new(ObjectId.BankBooth, BankBooths[0]),
        new(ObjectId.BankBooth, BankBooths[1]),
        new(ObjectId.Furnace, Furnace),
        new(ObjectId.Anvil, At(12, 20)),
        new(ObjectId.StairsUp, StairsUp, Destination: At(32, 42, 1)),
        new(ObjectId.StairsDown, StairsDown, Destination: At(32, 42)),
    ];

    private static readonly ObjectPlacement[] Nodes =
    [
        .. Placed(ObjectId.Tree, [(5, 30), (7, 33), (9, 29), (6, 36)]),
        .. Placed(ObjectId.Rock, [(52, 30), (54, 32), (56, 30)]),
        .. Placed(ObjectId.FishingSpot, [(20, Shore), (24, Shore), (28, Shore)]),
    ];

    // Four goblins, which is the number the Guard counts.
    private static readonly NpcSpawn[] Creatures =
    [
        new(NpcId.Guard, At(30, 14)),
        new(NpcId.Shopkeeper, Shopkeeper),
        new(NpcId.Lamplighter, At(34, 38)),
        .. Wandering(NpcId.Chicken, [(46, 48), (48, 50), (45, 51)]),
        .. Wandering(NpcId.Goblin, [(8, 14), (11, 16), (6, 18), (10, 12)]),
    ];

    /// <summary>One region of sea and ground, rooms on it, and a second region for the floor above the tower.</summary>
    public static WorldPlan Build() => new(Terrain(), Spawn, [.. DoorsAndStations, .. Nodes], Creatures);

    private static TileCoord At(int x, int y, int plane = 0) => new(x, y, plane);

    private static IEnumerable<ObjectPlacement> Placed(int objectId, (int X, int Y)[] tiles) =>
        tiles.Select(t => new ObjectPlacement(objectId, At(t.X, t.Y)));

    private static IEnumerable<NpcSpawn> Wandering(int npc, (int X, int Y)[] tiles) =>
        tiles.Select(t => new NpcSpawn(npc, At(t.X, t.Y), radius: 3));

    private static void Flag(TileMap map, TileCoord tile, TileFlags flags) => map.SetFlags(tile, map.FlagsAt(tile) | flags);

    /// <summary>
    /// A room whose floor is the tiles from <paramref name="from"/> to <paramref name="to"/>.
    /// Walls stand on its outer edges, with a gap in the south wall at <paramref name="doorX"/>.
    /// </summary>
    private static void Room(TileMap map, (int X, int Y) from, (int X, int Y) to, int doorX)
    {
        for (var y = from.Y; y <= to.Y; y++)
        {
            for (var x = from.X; x <= to.X; x++)
            {
                var walls = TileFlags.Open;
                if (y == from.Y && x != doorX)
                {
                    walls |= TileFlags.WallSouth;
                }

                if (y == to.Y)
                {
                    walls |= TileFlags.WallNorth;
                }

                if (x == from.X)
                {
                    walls |= TileFlags.WallWest;
                }

                if (x == to.X)
                {
                    walls |= TileFlags.WallEast;
                }

                Flag(map, At(x, y), walls);
            }
        }
    }

    private static TileMap Terrain()
    {
        var map = new TileMap();
        map.AddRegion(FlagRegion.Open(new RegionKey(0, 0, 0)));
        for (var y = 0; y < Shore; y++)
        {
            for (var x = 0; x < TileWorld.RegionSize; x++)
            {
                Flag(map, At(x, y), TileFlags.Water);
            }
        }

        Room(map, (20, 24), (25, 28), doorX: 22); // the shop
        Room(map, (36, 24), (41, 28), doorX: 38); // the bank
        Room(map, (30, 40), (34, 44), doorX: 32); // the tower
        AddUpstairs(map);
        return map;
    }

    // Nothing walks on plane 1 outside the tower, so it is blocked but for the one floor.
    private static void AddUpstairs(TileMap map)
    {
        var upstairs = new byte[TileWorld.RegionSize * TileWorld.RegionSize];
        Array.Fill(upstairs, (byte)TileFlags.Blocked.Bits);
        map.AddRegion(new FlagRegion(new RegionKey(0, 0, 1), upstairs));
        for (var y = 40; y <= 44; y++)
        {
            for (var x = 30; x <= 34; x++)
            {
                map.SetFlags(At(x, y, 1), TileFlags.Open);
            }
        }
    }
}
