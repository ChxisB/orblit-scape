using Scape.Core;

namespace Scape.Server;

/// <summary>What a player asked for, and who asked.</summary>
public readonly record struct PlayerIntent(string Player, Intent Intent);

/// <summary>Intents that have arrived and not yet been turned into actions.</summary>
/// <remarks>
/// The inbound phase drains it once a tick. A player keeps at most <see cref="PerPlayer"/> waiting,
/// so one that sends faster than the tick runs cannot grow it. The first ones stay, because they
/// came first.
/// </remarks>
public sealed class Inbox
{
    private readonly List<PlayerIntent> _waiting = [];
    private readonly Dictionary<string, int> _count = [];

    public Inbox(int perPlayer = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perPlayer, 1);
        PerPlayer = perPlayer;
    }

    public int PerPlayer { get; }

    /// <summary>Intents dropped because their player already had <see cref="PerPlayer"/> waiting.</summary>
    public long Dropped { get; private set; }

    public int Pending(string player) => _count.GetValueOrDefault(player);

    public void Add(string player, Intent intent)
    {
        if (Pending(player) >= PerPlayer)
        {
            Dropped++;
            return;
        }

        _count[player] = Pending(player) + 1;
        _waiting.Add(new PlayerIntent(player, intent));
    }

    /// <summary>Everything waiting, oldest first. The inbox is empty afterwards.</summary>
    public IReadOnlyList<PlayerIntent> Drain()
    {
        PlayerIntent[] all = [.. _waiting];
        _waiting.Clear();
        _count.Clear();
        return all;
    }

    public void Forget(string player)
    {
        _waiting.RemoveAll(entry => entry.Player == player);
        _count.Remove(player);
    }
}
