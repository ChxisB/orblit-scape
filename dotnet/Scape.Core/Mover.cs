namespace Scape.Core;

/// <summary>How many tiles a mover covers in one tick.</summary>
public enum MoveSpeed
{
    Walk,
    Run,
}

public static class MoveSpeeds
{
    extension(MoveSpeed speed)
    {
        public int TilesPerTick => speed switch
        {
            MoveSpeed.Walk => 1,
            MoveSpeed.Run => 2,
            _ => throw new InvalidOperationException($"{speed} is not a speed."),
        };
    }
}

/// <summary>Something that walks a queue of tiles, a tick at a time.</summary>
/// <remarks>
/// It knows its speed and nothing about why it has one. Run energy and the choice between
/// walking and running belong to whatever owns the mover.
/// </remarks>
public sealed class Mover(TileCoord position, Walker walker = Walker.Player)
{
    private readonly Queue<TileCoord> _queue = new();

    public Walker Walker { get; } = walker;

    public MoveSpeed Speed { get; set; } = MoveSpeed.Walk;

    public TileCoord Position { get; set; } = position;

    public bool IsMoving => _queue.Count > 0;

    /// <summary>The tiles entered by the last <see cref="Advance"/>, in order.</summary>
    public IReadOnlyList<TileCoord> StepsLastTick { get; private set; } = [];

    /// <summary>
    /// Replaces the queue with <paramref name="path"/>, whose first tile has to be beside
    /// <see cref="Position"/> and each next tile beside the one before.
    /// </summary>
    public void SetPath(IReadOnlyList<TileCoord> path)
    {
        var here = Position;
        foreach (var tile in path)
        {
            if (tile.Plane != here.Plane || Math.Abs(tile.X - here.X) > 1 || Math.Abs(tile.Y - here.Y) > 1 || tile == here)
            {
                throw new ArgumentException($"{tile} is not one step from {here}.", nameof(path));
            }

            here = tile;
        }

        _queue.Clear();
        foreach (var tile in path)
        {
            _queue.Enqueue(tile);
        }
    }

    public void ClearPath() => _queue.Clear();

    /// <summary>
    /// Takes as many steps along the queue as the speed covers in a tick. A step the map
    /// no longer allows, because a door shut or something was built, ends the walk where it stands.
    /// </summary>
    public void Advance(TileMap map)
    {
        var taken = new List<TileCoord>();
        while (taken.Count < Speed.TilesPerTick && _queue.TryPeek(out var next))
        {
            if (!map.CanStep(Position, next, Walker))
            {
                _queue.Clear();
                break;
            }

            _queue.Dequeue();
            Position = next;
            taken.Add(next);
        }

        StepsLastTick = taken.AsReadOnly();
    }
}
