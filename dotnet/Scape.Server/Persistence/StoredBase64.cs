namespace Scape.Server;

/// <summary>
/// Reads the base64 in a journal or an account record. Either alphabet is fine, and the padding
/// must be there.
/// </summary>
internal static class StoredBase64
{
    /// <summary>
    /// The bytes <paramref name="text"/> holds. Throws <see cref="FormatException"/> for anything
    /// else, whitespace included, which <see cref="Convert.FromBase64String"/> alone would skip.
    /// </summary>
    public static byte[] Decode(string text)
    {
        if (!text.All(IsBase64))
        {
            throw new FormatException("Not base64.");
        }

        return Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/'));
    }

    private static bool IsBase64(char c) => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '-' or '_' or '=';
}
