namespace Scape.Core;

/// <summary>The tiles to walk, one step each and not counting where the walk starts.</summary>
/// <remarks>
/// When the target cannot be reached, <see cref="Reached"/> is false and <see cref="Steps"/>
/// lead to the reachable tile nearest to it.
/// </remarks>
public sealed class PathResult(IEnumerable<TileCoord> steps, bool reached)
{
    public IReadOnlyList<TileCoord> Steps { get; } = [.. steps];

    public bool Reached { get; } = reached;
}

public static class PathSearch
{
    /// <summary>How far past the two points a search may look, in tiles.</summary>
    public const int DefaultMargin = 16;

    private static readonly (int Dx, int Dy)[] Offsets =
        [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    /// <summary>The shortest walk from <paramref name="from"/> to <paramref name="to"/> in steps, counting a diagonal as one.</summary>
    // F2: walker and margin are options a caller names, as the defaults show.
    public static PathResult Find(
        TileMap map,
        TileCoord from,
        TileCoord to,
        Walker walker = Walker.Player,
        int margin = DefaultMargin) =>
        FindToReach(map, from, new OnTile(to), walker, margin);

    /// <summary>The shortest walk from <paramref name="from"/> to any tile that satisfies <paramref name="reach"/>.</summary>
    /// <remarks>
    /// The search stays inside the box round <paramref name="from"/> and the target, grown by
    /// <paramref name="margin"/> tiles on each side, so a target behind a long wall is called
    /// unreachable instead of costing the whole map.
    /// </remarks>
    // F2: walker and margin are options a caller names, as the defaults show.
    public static PathResult FindToReach(
        TileMap map,
        TileCoord from,
        Reach reach,
        Walker walker = Walker.Player,
        int margin = DefaultMargin)
    {
        if (from.Plane != reach.Target.Origin.Plane)
        {
            throw new ArgumentException("A path stays on one plane.");
        }

        return new Search(map, reach, walker, Box.Around(from, reach.Target, margin)).Run(from);
    }

    /// <summary>The tiles a search may look at: the box round the start and the target, grown by a margin and cut to the world.</summary>
    private readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX;

        public int Height => MaxY - MinY;

        public static Box Around(TileCoord from, Footprint target, int margin) => new(
            Math.Max(0, Math.Min(from.X, target.Origin.X) - margin),
            Math.Max(0, Math.Min(from.Y, target.Origin.Y) - margin),
            Math.Min(TileWorld.WorldSize, Math.Max(from.X + 1, target.Origin.X + target.Width) + margin),
            Math.Min(TileWorld.WorldSize, Math.Max(from.Y + 1, target.Origin.Y + target.Height) + margin));

        public bool Contains(int x, int y) => x >= MinX && x < MaxX && y >= MinY && y < MaxY;

        public int IndexOf(int x, int y) => (y - MinY) * Width + (x - MinX);
    }

    private sealed class Search(TileMap map, Reach reach, Walker walker, Box box)
    {
        private readonly int[] _parent = Enumerable.Repeat(-1, box.Width * box.Height).ToArray();

        public PathResult Run(TileCoord from)
        {
            var start = box.IndexOf(from.X, from.Y);
            _parent[start] = start;
            var queue = new List<int> { start };
            var nearest = start;
            var nearestDistance = reach.Target.DistanceSquared(from);
            for (var head = 0; head < queue.Count; head++)
            {
                var current = TileAt(queue[head]);
                if (reach.IsSatisfied(map, current))
                {
                    return new PathResult(WalkBack(queue[head]), reached: true);
                }

                var distance = reach.Target.DistanceSquared(current);
                if (distance < nearestDistance)
                {
                    nearest = queue[head];
                    nearestDistance = distance;
                }

                Expand(current, queue[head], queue);
            }

            return new PathResult(WalkBack(nearest), reached: false);
        }

        private TileCoord TileAt(int index) =>
            new(box.MinX + index % box.Width, box.MinY + index / box.Width, reach.Target.Origin.Plane);

        private void Expand(TileCoord current, int index, List<int> queue)
        {
            foreach (var (dx, dy) in Offsets)
            {
                var x = current.X + dx;
                var y = current.Y + dy;
                if (!box.Contains(x, y))
                {
                    continue;
                }

                var next = box.IndexOf(x, y);
                if (_parent[next] != -1 || !map.CanStep(current, new TileCoord(x, y, current.Plane), walker))
                {
                    continue;
                }

                _parent[next] = index;
                queue.Add(next);
            }
        }

        private List<TileCoord> WalkBack(int end)
        {
            var steps = new List<TileCoord>();
            for (var at = end; _parent[at] != at; at = _parent[at])
            {
                steps.Add(TileAt(at));
            }

            steps.Reverse();
            return steps;
        }
    }
}
