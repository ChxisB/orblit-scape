using Scape.Core;

namespace Scape.Server;

/// <summary>Public chat, whispers, and each player's friend and ignore lists.</summary>
/// <remarks>
/// Lists and mutes are kept by name for as long as the server runs, so a player who logs out and
/// in again finds them. <see cref="ListsOf"/> and <see cref="Restore"/> let a save carry the lists
/// across a restart. Names on the wire are lower case, the same as player keys.
/// </remarks>
public sealed class Social
{
    /// <summary>
    /// Chat reaches listeners within this many tiles. A client is always sent the 16 tiles round
    /// its player, so a listener this near has the speaker's avatar to draw the line over.
    /// </summary>
    public const int ChatRadius = 15;

    private readonly IHosting _hosting;
    private readonly Func<string, (ulong NetworkId, TileCoord Tile)?> _standing;
    private readonly HashSet<string> _online = [];
    private readonly HashSet<string> _muted = [];
    private readonly Dictionary<string, Lists> _lists = [];

    private Social(IHosting hosting, Func<string, (ulong NetworkId, TileCoord Tile)?> standing)
    {
        _hosting = hosting;
        _standing = standing;
    }

    /// <summary>Adds chat to <paramref name="registry"/> and returns the feature, so a moderator can mute a player.</summary>
    /// <param name="standing">
    /// Where a player stands and which entity is theirs, or null when they are not in the world.
    /// Social asks it to decide who hears a line.
    /// </param>
    public static Social Register(Registry registry, Services services, Func<string, (ulong NetworkId, TileCoord Tile)?> standing)
    {
        var social = new Social(services.Hosting, standing);
        registry.AddJoinHandler(social.Join);
        registry.AddGoneHandler(social.Gone);
        registry.AddIntentHandler(social.OnIntent);
        return social;
    }

    public void Mute(string player) => _muted.Add(Wire.NameKey(player));

    public void Unmute(string player) => _muted.Remove(Wire.NameKey(player));

    public bool IsMuted(string player) => _muted.Contains(Wire.NameKey(player));

    /// <summary>The friends and ignored names of <paramref name="player"/>, for a save.</summary>
    public (IReadOnlyList<string> Friends, IReadOnlyList<string> Ignored) ListsOf(string player) =>
        _lists.TryGetValue(player, out var lists) ? ([.. lists.Friends], [.. lists.Ignored]) : ([], []);

    /// <summary>Replaces the lists of <paramref name="player"/> with a saved pair and sends them.</summary>
    public void Restore(string player, IEnumerable<string> friends, IEnumerable<string> ignored)
    {
        _lists[player] = new Lists([.. friends.Distinct()], [.. ignored.Distinct()]);
        SendLists(player);
    }

    /// <summary>Whether <paramref name="player"/> has <paramref name="other"/> on their ignore list.</summary>
    public bool IsIgnoring(string player, string other) =>
        _lists.TryGetValue(player, out var lists) && lists.Ignored.Contains(other);

    private void Join(string key)
    {
        _online.Add(key);
        SendLists(key);
        RefreshWatchers(key);
    }

    private void Gone(string key)
    {
        _online.Remove(key);
        RefreshWatchers(key);
    }

    private void OnIntent(string player, Intent intent)
    {
        if (!_online.Contains(player))
        {
            return;
        }

        switch (intent)
        {
            case Chat chat:
                Say(player, chat.Text);
                break;
            case Whisper whisper:
                SendWhisper(player, whisper.To, whisper.Text);
                break;
            case EditList edit:
                Edit(player, edit.List == ListKind.Friends, edit.Add, Wire.NameKey(edit.Name));
                break;
        }
    }

    private void Say(string player, string text)
    {
        if (_standing(player) is not { } speaker || RefuseMuted(player))
        {
            return;
        }

        var said = new Said((long)speaker.NetworkId, text);
        foreach (var listener in _online)
        {
            if (Hears(listener, player, speaker.Tile))
            {
                _hosting.Tell(listener, said);
            }
        }
    }

    private bool Hears(string listener, string speaker, TileCoord at) =>
        !IsIgnoring(listener, speaker)
        && _standing(listener) is { } standing
        && standing.Tile.Plane == at.Plane
        && Math.Abs(standing.Tile.X - at.X) <= ChatRadius
        && Math.Abs(standing.Tile.Y - at.Y) <= ChatRadius;

    private void SendWhisper(string player, string to, string text)
    {
        if (RefuseMuted(player))
        {
            return;
        }

        var target = Wire.NameKey(to);

        // An ignored sender gets the answer an offline name gets, so ignoring does not tell them
        // they were ignored.
        if (!_online.Contains(target) || IsIgnoring(target, player))
        {
            _hosting.Tell(player, new Notice("That player is not online."));
            return;
        }

        _hosting.Tell(target, new Whispered(player, text));
    }

    private void Edit(string player, bool friends, bool add, string key)
    {
        if (!_lists.TryGetValue(player, out var lists))
        {
            lists = new Lists([], []);
            _lists[player] = lists;
        }

        var names = friends ? lists.Friends : lists.Ignored;
        if (add)
        {
            if (WhyNotAdded(player, names, key) is { } why)
            {
                _hosting.Tell(player, new Notice(why));
                return;
            }

            if (names.Contains(key))
            {
                return;
            }

            names.Add(key);
        }
        else if (!names.Remove(key))
        {
            return;
        }

        SendLists(player);
    }

    private static string? WhyNotAdded(string player, List<string> names, string key)
    {
        if (key == player)
        {
            return "You cannot add yourself.";
        }

        return !names.Contains(key) && names.Count >= Wire.MaxListEntries ? "That list is full." : null;
    }

    private bool RefuseMuted(string player)
    {
        if (!IsMuted(player))
        {
            return false;
        }

        _hosting.Tell(player, new Notice("You are muted."));
        return true;
    }

    private void SendLists(string player)
    {
        var (friends, ignored) = ListsOf(player);
        _hosting.Tell(player, new SocialLists([.. friends.Select(name => new Friend(name, _online.Contains(name)))], ignored));
    }

    /// <summary>Players who list <paramref name="key"/> as a friend see them come and go.</summary>
    private void RefreshWatchers(string key)
    {
        foreach (var player in _online)
        {
            if (player != key && _lists.TryGetValue(player, out var lists) && lists.Friends.Contains(key))
            {
                SendLists(player);
            }
        }
    }

    // Lists, not sets, since the order names were added in is the order a client shows them.
    private sealed record Lists(List<string> Friends, List<string> Ignored);
}
