using Scape.Core;

namespace Scape.Server;

/// <summary>
/// Moves what the features hold of a player into a <see cref="PlayerState"/> and back. This is the
/// one place that knows which features a save covers.
/// </summary>
internal sealed class PlayerStates(Features features, TileMap map)
{
    /// <summary>The keys of the players in the world.</summary>
    public IEnumerable<string> Players => features.Movement.Players;

    /// <summary>
    /// Throws <see cref="FormatException"/> when <paramref name="state"/> holds something this
    /// server would lose on the next save, so a login is refused and the save stays as it was.
    /// </summary>
    public void Check(PlayerState state)
    {
        if (features.Quests is null && state.Quests.Count > 0)
        {
            throw new FormatException("The save has quests and this server has no story");
        }
    }

    /// <summary>What the features hold of <paramref name="player"/>, or null when they are not in the world.</summary>
    public PlayerState? Capture(string player)
    {
        if (features.Movement.TileOf(player) is not { } tile)
        {
            return null;
        }

        var (friends, ignored) = features.Social.ListsOf(player);
        return new PlayerState(
            tile,
            skills: features.Skills.ExperienceOf(player),
            quests: features.Quests?.StagesOf(player),
            containers: ContainersOf(player),
            friends: friends,
            ignored: ignored);
    }

    /// <summary>
    /// Puts <paramref name="state"/> on <paramref name="player"/>, who has just joined. A saved tile
    /// that is no longer open ground leaves them at the spawn. Returns how many saved skills,
    /// stacks and quests the game no longer has.
    /// </summary>
    // F5: the count is how the caller tells an operator what a save lost.
    public int Apply(string player, PlayerState state)
    {
        if (!map.FlagsAt(state.Tile).Has(TileFlags.Blocked | TileFlags.Water))
        {
            features.Movement.Teleport(player, state.Tile);
        }

        var dropped = features.Skills.Restore(player, state.Skills);
        foreach (var (id, slots) in state.Containers)
        {
            dropped += features.Items.ContainerOf(player, id)?.Restore(slots) ?? slots.Count;
        }

        dropped += features.Quests?.Restore(player, state.Quests) ?? state.Quests.Count;
        features.Social.Restore(player, state.Friends, state.Ignored);
        features.Combat.Refit(player);
        return dropped;
    }

    private Dictionary<ContainerId, IReadOnlyList<ItemStack?>> ContainersOf(string player)
    {
        var containers = new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>();
        foreach (var id in Enum.GetValues<ContainerId>())
        {
            if (features.Items.ContainerOf(player, id) is { } container)
            {
                containers[id] = container.Slots;
            }
        }

        return containers;
    }
}
