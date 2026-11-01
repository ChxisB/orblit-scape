using Scape.Core;

namespace Scape.Content;

/// <summary>
/// The ground and everything standing on it: the flags of every region, where
/// a new player appears, the objects and the creatures.
/// </summary>
public sealed class WorldPlan(TileMap map, TileCoord spawn, IReadOnlyList<ObjectPlacement> objects, IReadOnlyList<NpcSpawn> npcs)
{
    /// <summary>Mutable, because the server flags the tiles of a solid object blocked.</summary>
    public TileMap Map { get; } = map;

    public TileCoord Spawn { get; } = spawn;

    public IReadOnlyList<ObjectPlacement> Objects { get; } = [.. objects];

    public IReadOnlyList<NpcSpawn> Npcs { get; } = [.. npcs];
}

/// <summary>The island a new game starts on.</summary>
public static class StarterIsland
{
    // Land is an ellipse across the four regions of plane 0.
    private const int Across = 2;
    private const int Centre = Across * TileWorld.RegionSize / 2;
    private const int RadiusX = 54;
    private const int RadiusY = 50;

    private static readonly ObjectPlacement[] DoorsAndStations =
    [
        new(ObjectId.Door, At(56, 62)),
        new(ObjectId.Door, At(71, 62)),
        new(ObjectId.Door, At(68, 82)),
        new(ObjectId.BankBooth, At(70, 66)),
        new(ObjectId.BankBooth, At(72, 66)),
        new(ObjectId.Furnace, At(50, 60)),
        new(ObjectId.Anvil, At(52, 60)),
        new(ObjectId.StairsUp, At(69, 85), Destination: At(68, 84, 1)),
        new(ObjectId.StairsDown, At(69, 85, 1), Destination: At(68, 84)),
    ];

    // A tree grove in the west, a quarry in the east and fishing spots on the
    // south shore.
    private static readonly ObjectPlacement[] Nodes =
    [
        .. Placed(ObjectId.Tree, [(24, 64), (26, 67), (28, 62), (30, 66), (25, 70), (29, 70), (32, 63), (27, 60)]),
        .. Placed(ObjectId.Rock, [(100, 60), (102, 62), (104, 60), (101, 65), (103, 66), (106, 63)]),
        .. Placed(ObjectId.FishingSpot, [(60, 15), (64, 14), (68, 15)]),
    ];

    // The people stand in the village. Chickens wander by a coop in the north
    // east. Four goblins camp in the south west, which is the four the Guard
    // counts.
    private static readonly NpcSpawn[] Creatures =
    [
        new(NpcId.Guard, At(62, 56)),
        new(NpcId.Shopkeeper, At(56, 65)),
        new(NpcId.Lamplighter, At(70, 80)),
        .. Wandering(NpcId.Chicken, [(84, 76), (86, 78), (83, 79), (87, 75), (85, 81)]),
        .. Wandering(NpcId.Goblin, [(30, 34), (33, 36), (28, 38), (32, 31)]),
    ];

    /// <summary>
    /// A new island each call, since the server changes the flags of its map.
    /// Five regions: four of sea and ground on plane 0 and one for the floor
    /// above the tower.
    /// </summary>
    public static WorldPlan Build() => new(Terrain(), At(64, 58), [.. DoorsAndStations, .. Nodes], Creatures);

    private static TileCoord At(int x, int y, int plane = 0) => new(x, y, plane);

    private static IEnumerable<ObjectPlacement> Placed(int objectId, (int X, int Y)[] tiles) =>
        tiles.Select(t => new ObjectPlacement(objectId, At(t.X, t.Y)));

    private static IEnumerable<NpcSpawn> Wandering(int npc, (int X, int Y)[] tiles) =>
        tiles.Select(t => new NpcSpawn(npc, At(t.X, t.Y), radius: 3));

    private static bool IsLand(int x, int y)
    {
        var dx = (x - Centre) / (double)RadiusX;
        var dy = (y - Centre) / (double)RadiusY;
        return (dx * dx) + (dy * dy) <= 1;
    }

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

    /// <summary>Ground and sea on plane 0, rooms on it, and the floor above the tower.</summary>
    private static TileMap Terrain()
    {
        var map = new TileMap();
        for (var y = 0; y < Across; y++)
        {
            for (var x = 0; x < Across; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        for (var y = 0; y < Across * TileWorld.RegionSize; y++)
        {
            for (var x = 0; x < Across * TileWorld.RegionSize; x++)
            {
                if (!IsLand(x, y))
                {
                    Flag(map, At(x, y), TileFlags.Water);
                }
            }
        }

        Room(map, (54, 62), (59, 66), doorX: 56); // the shop
        Room(map, (69, 62), (74, 66), doorX: 71); // the bank
        Room(map, (66, 82), (70, 86), doorX: 68); // the tower
        AddUpstairs(map);
        return map;
    }

    // Nothing walks on plane 1 outside the tower, so it is blocked but for the
    // one floor.
    private static void AddUpstairs(TileMap map)
    {
        var upstairs = new byte[TileWorld.RegionSize * TileWorld.RegionSize];
        Array.Fill(upstairs, (byte)TileFlags.Blocked.Bits);
        map.AddRegion(new FlagRegion(new RegionKey(1, 1, 1), upstairs));
        for (var y = 82; y <= 86; y++)
        {
            for (var x = 66; x <= 70; x++)
            {
                map.SetFlags(At(x, y, 1), TileFlags.Open);
            }
        }
    }
}
