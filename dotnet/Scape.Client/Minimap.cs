namespace Scape.Client;

public enum DotKind
{
    Player,
    Npc,
}

/// <summary>Where something is from the player, in tiles. East and north are positive.</summary>
public readonly record struct Dot(double Dx, double Dy, DotKind Kind);

public static class Minimap
{
    /// <summary>
    /// The players and creatures within <paramref name="radius"/> tiles of the player, on their plane,
    /// for a minimap to draw as dots. The player is the centre and not a dot. There are no terrain
    /// flags here, because the client holds no map.
    /// </summary>
    public static IReadOnlyList<Dot> Dots(GameSession session, int radius = 16)
    {
        var actors = session.Actors;
        if (actors.Where(actor => actor.Mine).Cast<ActorSprite?>().FirstOrDefault() is not { } me)
        {
            return [];
        }

        return
        [
            .. actors.Where(actor => !actor.Mine).Select(actor => Near(me, actor.X, actor.Y, actor.Plane, DotKind.Player, radius)).OfType<Dot>(),
            .. session.Npcs.Select(npc => Near(me, npc.X, npc.Y, npc.Plane, DotKind.Npc, radius)).OfType<Dot>(),
        ];
    }

    private static Dot? Near(ActorSprite me, double x, double y, int plane, DotKind kind, int radius)
    {
        var dx = x - me.X;
        var dy = y - me.Y;
        return plane == me.Plane && Math.Abs(dx) <= radius && Math.Abs(dy) <= radius ? new Dot(dx, dy, kind) : null;
    }
}
