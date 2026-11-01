using Orblit.Terrain;
using Orblit.VectorMath;
using Scape.Core;

namespace Scape.Client;

/// <summary>Tiles on the ground. One tile is one unit and tile y runs along world z.</summary>
public static class TilePick
{
    /// <summary>The tile under <paramref name="ray"/>, or null when it meets no ground or meets ground outside the world.</summary>
    public static TileCoord? Pick(Terrain terrain, PickRay ray, int plane = 0)
    {
        if (terrain.Raycast(ray.Origin, ray.Direction) is not { } hit)
        {
            return null;
        }

        // Compared before the cast, since a far hit does not fit an int.
        var x = Math.Floor(hit.X);
        var y = Math.Floor(hit.Z);
        if (x < 0 || y < 0 || x >= TileWorld.WorldSize || y >= TileWorld.WorldSize)
        {
            return null;
        }

        return new TileCoord((int)x, (int)y, plane);
    }

    /// <summary>
    /// Where an actor at tile position (<paramref name="x"/>, <paramref name="y"/>) is drawn: the
    /// middle of its tile, on the ground. The position is not whole between two ticks.
    /// </summary>
    public static Vector3 StandingPoint(Terrain terrain, double x, double y)
    {
        var worldX = x + 0.5;
        var worldZ = y + 0.5;
        return new Vector3(worldX, terrain.HeightAt(worldX, worldZ) ?? 0, worldZ);
    }
}
