namespace Scape.Server;

/// <summary>The keys a server keeps its players under in a save store.</summary>
public static class StoreKeys
{
    /// <summary>
    /// The store key of the mutes and bans. One record holds them all, and the <c>l-</c> prefix
    /// keeps it from a save, an account or a trade journal.
    /// </summary>
    public const string Ledger = "l-sanctions";

    /// <summary>
    /// The store key of a player's save. Names hold letters, digits, spaces, underscores and
    /// hyphens, and a store key may not hold a space, so a space becomes a dot. A name has no dot,
    /// so two names never share a key.
    /// </summary>
    public static string SaveOf(string player) => "p-" + Dotted(player);

    /// <summary>
    /// The store key of a player's account. The first two characters tell a save from an account,
    /// so a name that looks like the other prefix still cannot share a key.
    /// </summary>
    public static string AccountOf(string player) => "a-" + Dotted(player);

    /// <summary>
    /// The store key of the journal for a trade between <paramref name="a"/> and
    /// <paramref name="b"/>. The names are sorted, so both players give one key. The first name's
    /// length keeps two pairs from sharing a key, and the <c>t-</c> prefix keeps it from a save.
    /// </summary>
    internal static string TradeOf(string a, string b)
    {
        string[] names = [Dotted(a), Dotted(b)];
        Array.Sort(names, StringComparer.Ordinal);
        return $"t-{names[0].Length}-{names[0]}-{names[1]}";
    }

    private static string Dotted(string player) => player.Replace(' ', '.');
}
