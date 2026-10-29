using Orblit.Core;
using Orblit.Net;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// What features need from the host to put an actor in the world and talk to a player. Declared
/// here, beside the features that call it.
/// </summary>
public interface IHosting
{
    /// <summary>Makes <paramref name="entity"/> replicate. <paramref name="owner"/> is the player it belongs to.</summary>
    ulong Spawn(ulong entity, string? owner = null);

    void Despawn(ulong entity);

    void Tell(string player, ServerMessage message);

    /// <summary>Ends the session of <paramref name="player"/>. It completes once the leave has run, after the tick that holds the host.</summary>
    Task Kick(string player);
}

/// <summary>The parts of the server a feature may use while it registers.</summary>
/// <param name="Active">
/// The cells worth simulating this tick, which are those near a player. A system that costs
/// something per actor asks it first.
/// </param>
public sealed record Services(World World, TileMap Map, IHosting Hosting, ActiveCells Active);
