namespace Scape.Core;

public static class LineOfSight
{
    /// <summary>
    /// Whether something thrown from <paramref name="from"/> to <paramref name="to"/> passes
    /// over every tile between them.
    /// </summary>
    /// <remarks>
    /// The line is the tiles a straight line between the two centres crosses. It stops at a
    /// blocked tile, which the last tile may be, and at a wall on any edge it crosses. Water
    /// does not stop it. A diagonal step crosses both of its corner edges.
    /// </remarks>
    public static bool HasClearLine(TileMap map, TileCoord from, TileCoord to)
    {
        var x = from.X;
        var y = from.Y;
        var dx = Math.Abs(to.X - x);
        var dy = Math.Abs(to.Y - y);
        var sx = to.X > x ? 1 : -1;
        var sy = to.Y > y ? 1 : -1;
        var error = dx - dy;
        var here = from;
        while (here != to)
        {
            var doubled = 2 * error;
            if (doubled > -dy)
            {
                error -= dy;
                x += sx;
            }

            if (doubled < dx)
            {
                error += dx;
                y += sy;
            }

            var next = new TileCoord(x, y, from.Plane);
            if ((next != to && map.FlagsAt(next).Has(TileFlags.Blocked)) || !Crosses(map, here, next))
            {
                return false;
            }

            here = next;
        }

        return true;
    }

    private static bool Crosses(TileMap map, TileCoord a, TileCoord b)
    {
        if (a.X == b.X || a.Y == b.Y)
        {
            return map.EdgeOpen(a, b);
        }

        var alongX = new TileCoord(b.X, a.Y, a.Plane);
        var alongY = new TileCoord(a.X, b.Y, a.Plane);
        return map.EdgeOpen(a, alongX)
            && map.EdgeOpen(alongX, b)
            && map.EdgeOpen(a, alongY)
            && map.EdgeOpen(alongY, b);
    }
}
