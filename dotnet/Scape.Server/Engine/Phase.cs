namespace Scape.Server;

/// <summary>The parts of a tick, in the order they run.</summary>
/// <remarks>
/// <see cref="Outbound"/> runs the game's own outbound systems, then the host publishes. The host
/// times its own stages (capture, interest, send) under their own names, so they follow
/// <see cref="Outbound"/> in a summary rather than sit inside it.
/// </remarks>
public enum Phase
{
    Inbound,
    Actions,
    Npcs,
    Movement,
    Combat,
    Outbound,
}
