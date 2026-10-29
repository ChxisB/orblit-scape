using Scape.Core;

namespace Scape.Server;

/// <summary>What the rest of the server may reach into after the features register.</summary>
internal sealed record Features(Movement Movement, Social Social)
{
    /// <summary>
    /// The one place features join the server. Each feature adds its action kinds, systems and
    /// components to <paramref name="registry"/> here.
    /// </summary>
    public static Features Register(Registry registry, Services services, TileCoord spawn)
    {
        var movement = Movement.Register(registry, services, spawn);
        return new Features(movement, Social.Register(registry, services, movement.Standing));
    }
}
