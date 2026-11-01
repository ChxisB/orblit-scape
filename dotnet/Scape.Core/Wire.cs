using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Scape.Core;

/// <summary>The limits of the messages between a client and the server, and the checks on names and text.</summary>
public static partial class Wire
{
    public const int MaxNameChars = 12;

    /// <summary>The most a line of chat or a notice may take once encoded as UTF-8.</summary>
    public const int MaxTextBytes = 240;

    /// <summary>The most names a friends list or an ignore list may hold.</summary>
    public const int MaxListEntries = 50;

    /// <summary>The most bytes a password may take as UTF-8.</summary>
    public const int MaxPasswordBytes = 64;

    /// <summary>The fewest bytes a new account's password may take as UTF-8.</summary>
    public const int MinPasswordBytes = 8;

    /// <summary>The most a <see cref="PrivateState"/> delta may hold.</summary>
    public const int MaxDeltaBytes = 4096;

    /// <summary>The most options one choice in a conversation may offer.</summary>
    public const int MaxChoices = 8;

    /// <summary>The most different items one side of a trade may offer. An inventory has one slot for each.</summary>
    public const int MaxTradeLines = Container.InventorySize;

    /// <summary>A chat line that starts with this is a command for the server and not something to say.</summary>
    public const string CommandMarker = "/";

    // Bytes from the other end of a connection are refused when they are not UTF-8, never patched.
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> ByteOrderMark => "﻿"u8;

    /// <summary>The most prayers that are on at once: one for each combat stat.</summary>
    public static int MaxActivePrayers { get; } = Enum.GetValues<CombatStat>().Length;

    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    /// <summary>What two names must share to be the same player: their letters, whatever the case.</summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "A player key is lower case in saves and on the wire.")]
    public static string NameKey(string name) => name.ToLowerInvariant();

    /// <summary>A line a player may read: not empty, within <see cref="MaxTextBytes"/>, and free of control characters.</summary>
    public static bool IsValidText(string text) =>
        text.Length > 0
        && Encoding.UTF8.GetByteCount(text) <= MaxTextBytes
        && !text.EnumerateRunes().Any(r => r.Value < 0x20 || r.Value == 0x7f);

    /// <summary>
    /// The text in <paramref name="bytes"/>. Throws <see cref="ArgumentException"/> when they are
    /// not UTF-8. A leading byte order mark is not part of the text, so one is dropped.
    /// </summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes) =>
        Strict.GetString(bytes.StartsWith(ByteOrderMark) ? bytes[ByteOrderMark.Length..] : bytes);

    /// <summary>Throws unless <paramref name="value"/> is 0 to <paramref name="max"/>.</summary>
    internal static void InRange(long value, long max, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, max, name);
    }

    internal static void CheckItem(int item, [CallerArgumentExpression(nameof(item))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(item, 1, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item, ItemDef.MaxId, name);
    }

    internal static void CheckCount(long count, [CallerArgumentExpression(nameof(count))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, ItemStack.MaxCount, name);
    }

    /// <summary>
    /// <paramref name="value"/> as an int. Every field that takes an int refuses a value past
    /// that range, so one is refused here with the same exception.
    /// </summary>
    internal static int Narrow(long value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, int.MinValue, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, int.MaxValue, name);
        return (int)value;
    }

    /// <summary>
    /// Letters and digits first, then up to eleven more with spaces, underscores and
    /// hyphens inside, and no space at the end.
    /// </summary>
    // \z and not $, which would also match before a final line feed.
    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9 _-]{0,10}[A-Za-z0-9_-])?\z")]
    private static partial Regex NamePattern();
}
