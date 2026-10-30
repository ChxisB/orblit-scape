using System.Diagnostics;
using Scape.Core;
using Scape.Server;

namespace Scape.Bots;

/// <summary>What the server holds about one player.</summary>
public sealed record PlayerView(
    TileCoord Tile,
    bool Moving,
    bool Fighting,
    (int Now, int Max)? Health,
    IReadOnlyDictionary<string, long> Experience);

/// <summary>A creature or object in an observed area, by its definition id.</summary>
public sealed record Seen(ulong NetworkId, int Def, TileCoord Tile);

/// <summary>What stands in an observed area.</summary>
public sealed record AreaView(IReadOnlyList<string> Players, IReadOnlyList<Seen> Creatures, IReadOnlyList<Seen> Objects);

/// <summary>
/// The tools an agent uses to look at a running <see cref="ScapeServer"/> and to move it along: who
/// is there, what a player holds, what stands in an area, and how long the ticks took. It reads the
/// server's own state, so it sees what the server knows and not what a client was told.
/// </summary>
/// <param name="server">The server to look at.</param>
/// <param name="deliver">Lets the links deliver everything queued, and what that causes.</param>
public sealed class ServerProbe(ScapeServer server, Func<Task> deliver)
{
    /// <summary>The most tiles from the centre an observed area may reach. The area is read tile by tile.</summary>
    public const int MaxAreaRadius = 32;

    private readonly List<TimeSpan> _tickTimes = [];

    /// <summary>The length of each tick that <see cref="StepAsync"/> ran, oldest first.</summary>
    public IReadOnlyList<TimeSpan> TickTimes => _tickTimes.AsReadOnly();

    /// <summary>The players in the world, by name, in the order they joined.</summary>
    public IReadOnlyList<string> Players() => [.. server.Movement.Players];

    /// <summary>What the server holds about <paramref name="name"/>, or null when they are not in the world.</summary>
    public PlayerView? Player(string name) =>
        server.Movement.Standing(name) is { } standing
            ? new PlayerView(
                standing.Tile,
                server.Movement.IsMoving(name),
                server.Combat.IsFighting(name),
                server.Combat.HealthOfPlayer(name),
                server.Skills.ExperienceOf(name))
            : null;

    /// <summary>
    /// The players, creatures and objects within <paramref name="radius"/> tiles of <paramref name="centre"/>.
    /// An object that covers several tiles is listed once, at the first tile found.
    /// </summary>
    public AreaView Area(TileCoord centre, int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, MaxAreaRadius);
        bool Within(TileCoord tile) =>
            tile.Plane == centre.Plane && Math.Abs(tile.X - centre.X) <= radius && Math.Abs(tile.Y - centre.Y) <= radius;

        return new AreaView(
            [.. Players().Where(name => server.Movement.Standing(name) is { } at && Within(at.Tile))],
            [.. server.Npcs.Standing.Where(npc => Within(npc.Tile)).Select(npc => new Seen(npc.NetworkId, npc.Def.Id, npc.Tile))],
            ObjectsIn(centre, radius));
    }

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks, timing each one. What clients sent is delivered before a
    /// tick and what the tick sent is delivered after it.
    /// </summary>
    public async Task StepAsync(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
        {
            await deliver();

            // F10: a tick's real length is what this tool measures.
            var started = Stopwatch.GetTimestamp();
            server.Loop.RunTick();
            _tickTimes.Add(Stopwatch.GetElapsedTime(started));
            await deliver();
        }
    }

    private List<Seen> ObjectsIn(TileCoord centre, int radius)
    {
        var found = new List<Seen>();
        var listed = new HashSet<ulong>();
        var last = TileWorld.WorldSize - 1;
        for (var y = Math.Max(0, centre.Y - radius); y <= Math.Min(last, centre.Y + radius); y++)
        {
            for (var x = Math.Max(0, centre.X - radius); x <= Math.Min(last, centre.X + radius); x++)
            {
                var tile = new TileCoord(x, y, centre.Plane);
                if (server.Objects.At(tile) is { } placed && listed.Add(placed.NetworkId))
                {
                    found.Add(new Seen(placed.NetworkId, placed.Def.Id, tile));
                }
            }
        }

        return found;
    }
}
