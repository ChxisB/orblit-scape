using System.Text;
using Orblit.Json;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// The bytes of a trade journal: the saved state of each player in the trade, as they stand after
/// the swap. Names are lower case, as the server keys players.
/// </summary>
internal static class TradeJournal
{
    public static byte[] Encode(IEnumerable<KeyValuePair<string, byte[]>> states) =>
        Encoding.UTF8.GetBytes(JsonText.Encode(new OrderedDictionary<string, object?>(
            states.Select(s => KeyValuePair.Create(s.Key, (object?)Convert.ToBase64String(s.Value))))));

    /// <summary>Reads a journal, in the order it names the players. Throws <see cref="FormatException"/> for bytes that are not one.</summary>
    public static OrderedDictionary<string, byte[]> Decode(ReadOnlySpan<byte> bytes)
    {
        object? json;
        try
        {
            json = JsonText.Decode(Wire.DecodeText(bytes));
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new FormatException("A trade journal is not JSON.", e);
        }

        if (json is not OrderedDictionary<string, object?> { Count: 2 } entries)
        {
            throw new FormatException("A trade journal names two players.");
        }

        var states = new OrderedDictionary<string, byte[]>();
        foreach (var (key, value) in entries)
        {
            if (!Wire.IsValidName(key) || Wire.NameKey(key) != key || value is not string text)
            {
                throw new FormatException($"A trade journal has a bad entry for \"{key}\".");
            }

            states[key] = StoredBase64.Decode(text);
        }

        return states;
    }
}
