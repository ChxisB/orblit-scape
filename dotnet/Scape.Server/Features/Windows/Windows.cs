using Scape.Core;

namespace Scape.Server;

/// <summary>What a window shows.</summary>
public enum WindowKind
{
    Shop,
    Bank,
    Talk,
    Trade,
}

/// <summary>
/// The window a player has open: what it is, which of its kind and where the player has to stand
/// to keep using it. <see cref="Ref"/> is the creature id of the shopkeeper or the speaker, and
/// zero for a bank or a trade.
/// </summary>
public sealed record OpenWindow(WindowKind Kind, int Ref, Reach Reach);

/// <summary>
/// The window each player has open. A player has one at a time. It closes when they stop being
/// able to reach what it belongs to, however that came about, and the client is told.
/// </summary>
public sealed class Windows
{
    private readonly Services _services;
    private readonly Movement _movement;
    private readonly Dictionary<string, OpenWindow> _open = [];

    private Windows(Services services, Movement movement)
    {
        _services = services;
        _movement = movement;
    }

    /// <summary>
    /// Adds windows to <paramref name="registry"/> and returns the feature, so shops and banks can
    /// open one and check a request against it.
    /// </summary>
    public static Windows Register(Registry registry, Services services, Movement movement)
    {
        var feature = new Windows(services, movement);
        registry.AddGoneHandler(player => feature._open.Remove(player));
        registry.AddSystem(Phase.Outbound, feature.CloseOutOfReach);
        return feature;
    }

    /// <summary>The window <paramref name="player"/> has open, or null.</summary>
    public OpenWindow? Of(string player) => _open.GetValueOrDefault(player);

    /// <summary>The players who have the <paramref name="kind"/> window called <paramref name="reference"/> open.</summary>
    public IReadOnlyList<string> Viewers(WindowKind kind, int reference) =>
        [.. _open.Where(open => open.Value.Kind == kind && open.Value.Ref == reference).Select(open => open.Key)];

    /// <summary>
    /// Opens <paramref name="window"/> for <paramref name="player"/> in place of the one they had.
    /// The reach is fixed now, so a shopkeeper who wanders off does not close it.
    /// </summary>
    public void Open(string player, OpenWindow window) => _open[player] = window;

    /// <summary>Closes the window <paramref name="player"/> has open, if any, and tells them.</summary>
    public void Close(string player)
    {
        if (_open.Remove(player))
        {
            _services.Hosting.Tell(player, new WindowClosed());
        }
    }

    private void CloseOutOfReach()
    {
        foreach (var (player, window) in _open.ToList())
        {
            if (_movement.TileOf(player) is not { } tile || !window.Reach.IsSatisfied(_services.Map, tile))
            {
                Close(player);
            }
        }
    }
}
