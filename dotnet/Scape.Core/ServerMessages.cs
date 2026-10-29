using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Scape.Core;

/// <summary>A message the server sends a client.</summary>
public abstract class ServerMessage
{
    // Closed to this assembly, so the cases below are every message.
    private protected ServerMessage()
    {
    }
}

/// <summary>The login was taken. <see cref="Name"/> is the name the player is known by.</summary>
public sealed class LoginOk : ServerMessage
{
    public LoginOk(string name)
    {
        if (!Wire.IsValidName(name))
        {
            throw new ArgumentException($"{name} is not a valid name.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}

/// <summary>Which replicated entity is the player's own, by network id. The client follows it with the camera.</summary>
public sealed class Avatar : ServerMessage
{
    public Avatar(long networkId)
    {
        Wire.InRange(networkId, 0xFFFFFFFF);
        NetworkId = networkId;
    }

    public long NetworkId { get; }
}

/// <summary>
/// A change to what only the owner sees, such as items and skills. What the bytes mean belongs
/// to the feature that wrote them.
/// </summary>
public sealed class PrivateState : ServerMessage
{
    public PrivateState(ReadOnlySpan<byte> delta)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delta.Length, Wire.MaxDeltaBytes, nameof(delta));
        Delta = delta.ToArray();
    }

    public ReadOnlyMemory<byte> Delta { get; }
}

public sealed class Notice : ServerMessage
{
    public Notice(string text)
    {
        if (!Wire.IsValidText(text))
        {
            throw new ArgumentException($"{text} is not valid text.", nameof(text));
        }

        Text = text;
    }

    public string Text { get; }
}

/// <summary>
/// A swing landed on, or missed, the entity <see cref="Target"/> names. A <see cref="Damage"/>
/// of zero is a miss. The client draws it as a splat over the target.
/// </summary>
public sealed class Hit : ServerMessage
{
    public Hit(long target, int damage)
    {
        Wire.InRange(target, 0xFFFFFFFF);
        Wire.InRange(damage, 0xFFFF);
        Target = target;
        Damage = damage;
    }

    public long Target { get; }

    public int Damage { get; }
}

/// <summary>
/// A projectile leaves the entity <see cref="From"/> names and reaches the one <see cref="To"/>
/// names after <see cref="Ticks"/> ticks. The hit it carries arrives with it, as a <see cref="Hit"/>.
/// </summary>
public sealed class Projectile : ServerMessage
{
    public Projectile(long from, long to, int ticks, CombatStyle style)
    {
        Wire.InRange(from, 0xFFFFFFFF);
        Wire.InRange(to, 0xFFFFFFFF);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticks, 0xFF);
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "is not a combat style.");
        }

        From = from;
        To = to;
        Ticks = ticks;
        Style = style;
    }

    public long From { get; }

    public long To { get; }

    public int Ticks { get; }

    public CombatStyle Style { get; }
}

/// <summary>
/// The prayer points a player has, the most they may have and the prayers that are on. It is
/// sent whenever any of it changes.
/// </summary>
public sealed class PrayerView : ServerMessage
{
    public PrayerView(int points, int max, IReadOnlyList<int> active)
    {
        Wire.InRange(max, 0xFFFF);
        Wire.InRange(points, max);
        Active = Array.AsReadOnly([.. active]);
        if (Active.Count > Wire.MaxActivePrayers || Active.Distinct().Count() != Active.Count)
        {
            throw new ArgumentException("The prayers on are a set of one for each combat stat at most.", nameof(active));
        }

        if (Active.Any(id => id < 1 || id > PrayerDef.MaxId))
        {
            throw new ArgumentException("The prayers on hold a bad prayer id.", nameof(active));
        }

        Points = points;
        Max = max;
    }

    public int Points { get; }

    public int Max { get; }

    public IReadOnlyList<int> Active { get; }
}

/// <summary>A line of public chat, as the listener hears it. The client draws it over the entity <see cref="Speaker"/> names.</summary>
public sealed class Said : ServerMessage
{
    public Said(long speaker, string text)
    {
        Wire.InRange(speaker, 0xFFFFFFFF);
        if (!Wire.IsValidText(text))
        {
            throw new ArgumentException($"{text} is not valid text.", nameof(text));
        }

        Speaker = speaker;
        Text = text;
    }

    public long Speaker { get; }

    public string Text { get; }
}

/// <summary>A private message from the player called <see cref="From"/>.</summary>
public sealed class Whispered : ServerMessage
{
    public Whispered(string from, string text)
    {
        if (!Wire.IsValidName(from))
        {
            throw new ArgumentException($"{from} is not a valid name.", nameof(from));
        }

        if (!Wire.IsValidText(text))
        {
            throw new ArgumentException($"{text} is not valid text.", nameof(text));
        }

        From = from;
        Text = text;
    }

    public string From { get; }

    public string Text { get; }
}

/// <summary>A friend and whether they are logged in.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The game's term. Scape is called only from C#.")]
public readonly record struct Friend(string Name, bool Online);

/// <summary>
/// A player's friends, with who is online, and the names they ignore. It is sent whole whenever
/// any of it changes.
/// </summary>
public sealed class SocialLists : ServerMessage
{
    public SocialLists(IReadOnlyList<Friend> friends, IReadOnlyList<string> ignored)
    {
        Friends = Array.AsReadOnly([.. friends]);
        Ignored = Array.AsReadOnly([.. ignored]);
        if (Friends.Count > Wire.MaxListEntries || Ignored.Count > Wire.MaxListEntries)
        {
            throw new ArgumentException(FormattableString.Invariant($"A list holds at most {Wire.MaxListEntries} names."));
        }

        if (Friends.Select(f => f.Name).Concat(Ignored).FirstOrDefault(name => !Wire.IsValidName(name)) is { } bad)
        {
            throw new ArgumentException($"{bad} is not a valid name.");
        }
    }

    public IReadOnlyList<Friend> Friends { get; }

    public IReadOnlyList<string> Ignored { get; }
}

/// <summary>One line of a shop's stock: an item and how many the shop holds.</summary>
public readonly record struct ShopStock(int Item, long Count);

/// <summary>
/// What the shop of the creature kind <see cref="Npc"/> holds now. It is sent when a shop opens
/// and again each time its stock changes.
/// </summary>
public sealed class ShopView : ServerMessage
{
    public ShopView(int npc, IReadOnlyList<ShopStock> lines)
    {
        Wire.InRange(npc, 0xFFFF);
        Lines = Array.AsReadOnly([.. lines]);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Lines.Count, ShopDef.MaxLines, nameof(lines));
        foreach (var (item, count) in Lines)
        {
            Wire.InRange(item, ItemDef.MaxId);
            Wire.InRange(count, ShopLine.MaxStock);
        }

        Npc = npc;
    }

    public int Npc { get; }

    public IReadOnlyList<ShopStock> Lines { get; }
}

/// <summary>The player's bank is open. The slots reach them as container state.</summary>
public sealed class BankOpen : ServerMessage;

/// <summary>A shop, bank, trade or conversation window is no longer open.</summary>
public sealed class WindowClosed : ServerMessage;

/// <summary>
/// The trade the player has open with <see cref="Partner"/>: what each side offers, whether this
/// is the confirm step, and who has accepted this step. It is sent each time any of that changes.
/// </summary>
public sealed class TradeView : ServerMessage
{
    public TradeView(string partner, IReadOnlyList<ItemStack> mine, IReadOnlyList<ItemStack> theirs, TradeStep step)
    {
        if (!Wire.IsValidName(partner))
        {
            throw new ArgumentException($"{partner} is not a valid name.", nameof(partner));
        }

        Mine = Array.AsReadOnly([.. mine]);
        Theirs = Array.AsReadOnly([.. theirs]);
        if (Mine.Count > Wire.MaxTradeLines || Theirs.Count > Wire.MaxTradeLines)
        {
            throw new ArgumentException(FormattableString.Invariant($"A trade side offers at most {Wire.MaxTradeLines} items."));
        }

        Partner = partner;
        Step = step;
    }

    public string Partner { get; }

    public IReadOnlyList<ItemStack> Mine { get; }

    public IReadOnlyList<ItemStack> Theirs { get; }

    public TradeStep Step { get; }
}

/// <summary>Where a trade stands: whether this is the confirm step, and who has accepted this step.</summary>
public readonly record struct TradeStep(bool Confirming, bool YouAccepted, bool TheyAccepted);

/// <summary><see cref="Speaker"/> says <see cref="Text"/> in the conversation the player has open. The player answers with <see cref="Continue"/>.</summary>
public sealed class ConversationLine : ServerMessage
{
    public ConversationLine(string speaker, string text)
    {
        if (!Wire.IsValidText(speaker) || !Wire.IsValidText(text))
        {
            throw new ArgumentException("A conversation line needs a speaker and text.");
        }

        Speaker = speaker;
        Text = text;
    }

    public string Speaker { get; }

    public string Text { get; }
}

/// <summary>
/// What the player may pick in the conversation they have open. The player answers with
/// <see cref="Choose"/> and the index of one of <see cref="Options"/>.
/// </summary>
public sealed class ConversationChoices : ServerMessage
{
    public ConversationChoices(IReadOnlyList<string> options)
    {
        Options = Array.AsReadOnly([.. options]);
        ArgumentOutOfRangeException.ThrowIfLessThan(Options.Count, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Options.Count, Wire.MaxChoices, nameof(options));
        if (!Options.All(Wire.IsValidText))
        {
            throw new ArgumentException("The options hold invalid text.", nameof(options));
        }
    }

    public IReadOnlyList<string> Options { get; }
}

/// <summary>
/// Why a login was refused. It reaches the client as the text of the goodbye the host sends a
/// connection it turns away, because a refused connection is never a client and so cannot be
/// sent a message.
/// </summary>
public enum LoginRefusal
{
    /// <summary>A player is already logged in under that name.</summary>
    NameTaken,

    /// <summary>The bytes were not a login.</summary>
    Unreadable,

    /// <summary>
    /// No account has that name, or the password is wrong. One answer for both, so a guess does
    /// not learn which names exist.
    /// </summary>
    BadLogin,

    /// <summary>A new account's password is under <see cref="Wire.MinPasswordBytes"/> bytes.</summary>
    WeakPassword,

    /// <summary>An account is already made under that name.</summary>
    AccountExists,

    /// <summary>The server is checking as many passwords as it will at once. Try again.</summary>
    Busy,

    /// <summary>The server could not load the account or its character. Nothing was changed, so trying again later is safe.</summary>
    Unavailable,

    /// <summary>A moderator banned this name. The reason stays with the moderators.</summary>
    Banned,
}

public static class LoginRefusals
{
    extension(LoginRefusal)
    {
        /// <summary>The refusal a goodbye names, or null when it names none.</summary>
        public static LoginRefusal? FromWire(string reason)
        {
            foreach (var refusal in Enum.GetValues<LoginRefusal>())
            {
                if (JsonNamingPolicy.CamelCase.ConvertName(refusal.ToString()) == reason)
                {
                    return refusal;
                }
            }

            return null;
        }
    }
}
