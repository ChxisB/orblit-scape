using Scape.Core;

namespace Scape.Server;

/// <summary>The inventory, equipment and bank of every player in the world.</summary>
/// <remarks>
/// A player's containers live as long as the player is in the world, and they are lost when the
/// player goes until accounts save them. Whatever changes a container, the owner is sent the
/// slots that changed once a tick.
/// </remarks>
public sealed class Items
{
    private readonly IHosting _hosting;
    private readonly ItemSet _items;
    private readonly Dictionary<string, Dictionary<ContainerId, Container>> _bags = [];

    private Items(IHosting hosting, ItemSet items)
    {
        _hosting = hosting;
        _items = items;
    }

    /// <summary>
    /// Adds containers to <paramref name="registry"/> and returns the feature, so gathering, loot
    /// and shops can put items in.
    /// </summary>
    public static Items Register(Registry registry, Services services, ItemSet items)
    {
        var feature = new Items(services.Hosting, items);
        registry.AddIntentHandler(feature.OnIntent);
        registry.AddJoinHandler(feature.Join);
        registry.AddGoneHandler(feature.Gone);
        registry.AddSystem(Phase.Outbound, feature.Flush);
        return feature;
    }

    /// <summary>The container of <paramref name="player"/>, or null for a player who is not in the world.</summary>
    public Container? ContainerOf(string player, ContainerId id) => _bags.GetValueOrDefault(player)?.GetValueOrDefault(id);

    // A drag swaps two slots. A refusal leaves both as they were and tells the player, so the
    // panel the client draws never moves on its own.
    private void OnIntent(string player, Intent intent)
    {
        if (intent is not MoveItem move || ContainerOf(player, move.From) is not { } from || ContainerOf(player, move.To) is not { } to)
        {
            return;
        }

        var outcome = from.SwapWith(to, move.FromSlot, move.ToSlot);
        if (outcome != Outcome.Done)
        {
            _hosting.Tell(player, new Notice(outcome == Outcome.NotAllowed ? "That does not go there." : "There is nothing to move there."));
        }
    }

    private void Join(string key) =>
        _bags[key] = Enum.GetValues<ContainerId>().ToDictionary(id => id, id => Container.ForId(id, _items));

    private void Gone(string key) => _bags.Remove(key);

    private void Flush()
    {
        foreach (var (player, bags) in _bags)
        {
            foreach (var (id, container) in bags)
            {
                Send(player, id, container);
            }
        }
    }

    private void Send(string player, ContainerId id, Container container)
    {
        foreach (var delta in ContainerCodec.EncodeSlots(id, container, container.Dirty))
        {
            _hosting.Tell(player, new PrivateState(delta));
        }

        container.MarkClean();
    }
}
