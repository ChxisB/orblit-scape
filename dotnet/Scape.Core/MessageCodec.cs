using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Scape.Core;

/// <summary>Why bytes were not turned into a message.</summary>
public enum BadMessage
{
    Empty,
    UnknownTag,
    TooLarge,
    Malformed,
}

/// <summary>What a decoder made of some bytes. It never throws, since the bytes come from the other end of a connection.</summary>
public abstract class Decoded<T>
{
    // Closed to this assembly, so a result is understood or unreadable.
    private protected Decoded()
    {
    }
}

public sealed class Understood<T>(T message) : Decoded<T>
{
    public T Message { get; } = message;
}

public sealed class Unreadable<T>(BadMessage why) : Decoded<T>
{
    public BadMessage Why { get; } = why;
}

/// <summary>
/// The bytes of each message. The first byte is a tag that names the message, and each
/// message has a cap that a longer one is refused at before anything is read from it.
/// Numbers are little-endian, and text is UTF-8 after a byte that counts it.
/// </summary>
public static class MessageCodec
{
    private static readonly Dictionary<byte, (int Cap, Reader<ClientMessage> Read)> Client = new()
    {
        [ClientTag.Login] = (4 + Wire.MaxNameChars + Wire.MaxPasswordBytes, ReadLogin),
        [ClientTag.Walk] = (6, ReadWalk),
        [ClientTag.Interact] = (7, ReadInteract),
        [ClientTag.Chat] = (2 + Wire.MaxTextBytes, TextMessage(text => new Chat(text))),
        [ClientTag.Whisper] = (3 + Wire.MaxNameChars + Wire.MaxTextBytes, ReadWhisper),
        [ClientTag.EditList] = (3 + Wire.MaxNameChars, ReadEditList),
        [ClientTag.Buy] = (7, b => ReadPair(b, (item, count) => new Buy(item, count))),
        [ClientTag.Sell] = (7, b => ReadPair(b, (item, count) => new Sell(item, count))),
        [ClientTag.Deposit] = (8, ReadDeposit),
        [ClientTag.Withdraw] = (7, b => ReadPair(b, (slot, count) => new Withdraw(slot, count))),
        [ClientTag.Continue] = (1, b => b.Length == 1 ? new Continue() : null),
        [ClientTag.Choose] = (2, b => b.Length == 2 ? new Choose(b[1]) : null),
        [ClientTag.TradeOffer] = (7, b => ReadPair(b, (item, count) => new TradeOffer(item, count))),
        [ClientTag.TradeAccept] = (1, b => b.Length == 1 ? new TradeAccept() : null),
        [ClientTag.TradeDecline] = (1, b => b.Length == 1 ? new TradeDecline() : null),
        [ClientTag.Cast] = (7, b => ReadPair(b, (spell, target) => new Cast(spell, target))),
        [ClientTag.TogglePrayer] = (2, b => b.Length == 2 ? new TogglePrayer(b[1]) : null),
        [ClientTag.MoveItem] = (7, ReadMoveItem),
    };

    private static readonly Dictionary<byte, (int Cap, Reader<ServerMessage> Read)> Server = new()
    {
        [ServerTag.LoginOk] = (2 + Wire.MaxNameChars, TextMessage(name => new LoginOk(name))),
        [ServerTag.PrivateState] = (1 + Wire.MaxDeltaBytes, b => new PrivateState(b[1..])),
        [ServerTag.Notice] = (2 + Wire.MaxTextBytes, TextMessage(text => new Notice(text))),
        [ServerTag.Avatar] = (5, b => b.Length == 5 ? new Avatar(U32(b, 1)) : null),
        [ServerTag.Hit] = (7, b => b.Length == 7 ? new Hit(U32(b, 1), U16(b, 5)) : null),
        [ServerTag.Projectile] = (11, ReadProjectile),
        [ServerTag.PrayerView] = (6 + Wire.MaxActivePrayers, ReadPrayerView),
        [ServerTag.ShopView] = (4 + ShopDef.MaxLines * 6, ReadShopView),
        [ServerTag.BankOpen] = (1, b => b.Length == 1 ? new BankOpen() : null),
        [ServerTag.WindowClosed] = (1, b => b.Length == 1 ? new WindowClosed() : null),
        [ServerTag.ConversationLine] = (3 + 2 * Wire.MaxTextBytes, ReadConversationLine),
        [ServerTag.ConversationChoices] = (2 + Wire.MaxChoices * (1 + Wire.MaxTextBytes), ReadChoices),
        [ServerTag.TradeView] = (5 + Wire.MaxNameChars + 2 * Wire.MaxTradeLines * 6, ReadTradeView),
        [ServerTag.Said] = (6 + Wire.MaxTextBytes, ReadSaid),
        [ServerTag.Whispered] = (3 + Wire.MaxNameChars + Wire.MaxTextBytes, ReadWhispered),
        [ServerTag.SocialLists] = (3 + Wire.MaxListEntries * (2 + Wire.MaxNameChars) + Wire.MaxListEntries * (1 + Wire.MaxNameChars), ReadSocialLists),
    };

    private delegate T? Reader<out T>(ReadOnlySpan<byte> bytes)
        where T : class;

    /// <summary>The largest message a client may send. A host's inbound budget must let one through.</summary>
    public static int ClientMessageLimit { get; } = Client.Values.Max(c => c.Cap);

    [SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity", Justification = "F2: a list of cases is data.")]
    public static byte[] Encode(ClientMessage message) => message switch
    {
        Login login => [ClientTag.Login, .. Counted(login.Name), (byte)(login.Create ? 1 : 0), .. Counted(login.Password)],
        Walk walk => [ClientTag.Walk, .. Le16(walk.Target.X), .. Le16(walk.Target.Y), (byte)walk.Target.Plane],
        Interact interact => [ClientTag.Interact, (byte)interact.Kind, .. Le32(interact.Id), (byte)interact.Option],
        Chat chat => [ClientTag.Chat, .. Counted(chat.Text)],
        Whisper whisper => [ClientTag.Whisper, .. Counted(whisper.To), .. Counted(whisper.Text)],
        EditList edit => [ClientTag.EditList, (byte)((edit.List == ListKind.Ignored ? 1 : 0) | (edit.Add ? 2 : 0)), .. Counted(edit.Name)],
        Buy buy => [ClientTag.Buy, .. Line(buy.Item, buy.Count)],
        Sell sell => [ClientTag.Sell, .. Line(sell.Item, sell.Count)],
        Deposit deposit => [ClientTag.Deposit, .. Line(deposit.Slot, deposit.Count), (byte)deposit.Tab],
        Withdraw withdraw => [ClientTag.Withdraw, .. Line(withdraw.Slot, withdraw.Count)],
        Continue => [ClientTag.Continue],
        Choose choose => [ClientTag.Choose, (byte)choose.Index],
        TradeOffer offer => [ClientTag.TradeOffer, .. Line(offer.Item, offer.Count)],
        TradeAccept => [ClientTag.TradeAccept],
        TradeDecline => [ClientTag.TradeDecline],
        Cast cast => [ClientTag.Cast, .. Line(cast.Spell, cast.Target)],
        TogglePrayer toggle => [ClientTag.TogglePrayer, (byte)toggle.Prayer],
        MoveItem move => [ClientTag.MoveItem, (byte)move.From, (byte)move.To, .. Le16(move.FromSlot), .. Le16(move.ToSlot)],
        _ => throw new InvalidOperationException($"{message.GetType().Name} is not a client message."),
    };

    [SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity", Justification = "F2: a list of cases is data.")]
    public static byte[] Encode(ServerMessage message) => message switch
    {
        LoginOk ok => [ServerTag.LoginOk, .. Counted(ok.Name)],
        PrivateState state => [ServerTag.PrivateState, .. state.Delta.Span],
        Notice notice => [ServerTag.Notice, .. Counted(notice.Text)],
        Avatar avatar => [ServerTag.Avatar, .. Le32(avatar.NetworkId)],
        Said said => [ServerTag.Said, .. Le32(said.Speaker), .. Counted(said.Text)],
        Whispered whispered => [ServerTag.Whispered, .. Counted(whispered.From), .. Counted(whispered.Text)],
        SocialLists lists => EncodeSocialLists(lists),
        Hit hit => [ServerTag.Hit, .. Le32(hit.Target), .. Le16(hit.Damage)],
        Projectile shot => [ServerTag.Projectile, .. Le32(shot.From), .. Le32(shot.To), (byte)shot.Ticks, (byte)shot.Style],
        PrayerView view => [ServerTag.PrayerView, .. Le16(view.Points), .. Le16(view.Max), (byte)view.Active.Count, .. view.Active.Select(id => (byte)id)],
        ShopView shop => [ServerTag.ShopView, .. Le16(shop.Npc), (byte)shop.Lines.Count, .. shop.Lines.SelectMany(l => Line(l.Item, l.Count))],
        BankOpen => [ServerTag.BankOpen],
        WindowClosed => [ServerTag.WindowClosed],
        ConversationLine line => [ServerTag.ConversationLine, .. Counted(line.Speaker), .. Counted(line.Text)],
        ConversationChoices choices => [ServerTag.ConversationChoices, (byte)choices.Options.Count, .. choices.Options.SelectMany(Counted)],
        TradeView trade => EncodeTradeView(trade),
        _ => throw new InvalidOperationException($"{message.GetType().Name} is not a server message."),
    };

    public static Decoded<ClientMessage> DecodeClient(ReadOnlySpan<byte> bytes) => Decode(bytes, Client);

    public static Decoded<ServerMessage> DecodeServer(ReadOnlySpan<byte> bytes) => Decode(bytes, Server);

    private static Decoded<T> Decode<T>(ReadOnlySpan<byte> bytes, Dictionary<byte, (int Cap, Reader<T> Read)> readers)
        where T : class
    {
        if (bytes.IsEmpty)
        {
            return new Unreadable<T>(BadMessage.Empty);
        }

        if (!readers.TryGetValue(bytes[0], out var reader))
        {
            return new Unreadable<T>(BadMessage.UnknownTag);
        }

        if (bytes.Length > reader.Cap)
        {
            return new Unreadable<T>(BadMessage.TooLarge);
        }

        try
        {
            return reader.Read(bytes) is { } message ? new Understood<T>(message) : new Unreadable<T>(BadMessage.Malformed);
        }
        catch (ArgumentException)
        {
            // The message classes check their own fields, and text that is not UTF-8 throws one
            // too. Bytes that break those rules are malformed, not an error here.
            return new Unreadable<T>(BadMessage.Malformed);
        }
    }

    /// <summary>The UTF-8 of <paramref name="text"/> after a byte that counts it.</summary>
    private static byte[] Counted(string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return [(byte)body.Length, .. body];
    }

    private static byte[] Le16(int value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] Le32(long value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    /// <summary>A u16 then a u32. A trade is an item and a count, and a withdrawal a slot and a count.</summary>
    private static byte[] Line(int small, long count) => [.. Le16(small), .. Le32(count)];

    private static int U16(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);

    private static long U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);

    private static byte[] EncodeSocialLists(SocialLists lists) =>
    [
        ServerTag.SocialLists,
        (byte)lists.Friends.Count,
        .. lists.Friends.SelectMany(f => Counted(f.Name).Append((byte)(f.Online ? 1 : 0))),
        (byte)lists.Ignored.Count,
        .. lists.Ignored.SelectMany(Counted),
    ];

    /// <summary>
    /// The partner's name, a flags byte (1 confirming, 2 you accepted, 4 they accepted), the
    /// number of lines on each side, then six bytes for each line: your lines first.
    /// </summary>
    private static byte[] EncodeTradeView(TradeView trade)
    {
        var flags = (trade.Step.Confirming ? 1 : 0) | (trade.Step.YouAccepted ? 2 : 0) | (trade.Step.TheyAccepted ? 4 : 0);
        return
        [
            ServerTag.TradeView,
            .. Counted(trade.Partner),
            (byte)flags,
            (byte)trade.Mine.Count,
            (byte)trade.Theirs.Count,
            .. trade.Mine.Concat(trade.Theirs).SelectMany(s => Line(s.Item, s.Count)),
        ];
    }

    private static Reader<T> TextMessage<T>(Func<string, T> make)
        where T : class =>
        bytes => ReadText(bytes) is { } text ? make(text) : null;

    /// <summary>The string in a tag, length and text message, or null when the length byte does not match what follows.</summary>
    private static string? ReadText(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes.Length == 2 + bytes[1] ? Wire.DecodeText(bytes[2..]) : null;

    /// <summary>
    /// Reads a text with a length byte at <paramref name="at"/>, or null when it runs past the
    /// end. Next is where the following field starts.
    /// </summary>
    private static (string Text, int Next)? TextAt(ReadOnlySpan<byte> bytes, int at)
    {
        if (at >= bytes.Length)
        {
            return null;
        }

        var end = at + 1 + bytes[at];
        return end > bytes.Length ? null : (Wire.DecodeText(bytes[(at + 1)..end]), end);
    }

    private static (string First, string Second)? ReadTwoTexts(ReadOnlySpan<byte> bytes) =>
        TextAt(bytes, 1) is { } first && TextAt(bytes, first.Next) is { } second && second.Next == bytes.Length
            ? (first.Text, second.Text)
            : null;

    private static Login? ReadLogin(ReadOnlySpan<byte> bytes)
    {
        if (TextAt(bytes, 1) is not { } name || name.Next >= bytes.Length)
        {
            return null;
        }

        var flag = bytes[name.Next];
        return TextAt(bytes, name.Next + 1) is { } password && flag <= 1 && password.Next == bytes.Length
            ? new Login(name.Text, password.Text, flag == 1)
            : null;
    }

    private static Walk? ReadWalk(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 6 ? new Walk(new TileCoord(U16(bytes, 1), U16(bytes, 3), bytes[5])) : null;

    private static Interact? ReadInteract(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 7 ? new Interact(bytes[1], U32(bytes, 2), bytes[6]) : null;

    private static Whisper? ReadWhisper(ReadOnlySpan<byte> bytes) =>
        ReadTwoTexts(bytes) is { } texts ? new Whisper(texts.First, texts.Second) : null;

    private static Whispered? ReadWhispered(ReadOnlySpan<byte> bytes) =>
        ReadTwoTexts(bytes) is { } texts ? new Whispered(texts.First, texts.Second) : null;

    private static ConversationLine? ReadConversationLine(ReadOnlySpan<byte> bytes) =>
        ReadTwoTexts(bytes) is { } texts ? new ConversationLine(texts.First, texts.Second) : null;

    private static EditList? ReadEditList(ReadOnlySpan<byte> bytes) =>
        bytes.Length < 3 || bytes[1] > 3 || bytes.Length != 3 + bytes[2]
            ? null
            : new EditList((bytes[1] & 1) == 0 ? ListKind.Friends : ListKind.Ignored, (bytes[1] & 2) != 0, Wire.DecodeText(bytes[3..]));

    private static Said? ReadSaid(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 6 && bytes.Length == 6 + bytes[5] ? new Said(U32(bytes, 1), Wire.DecodeText(bytes[6..])) : null;

    // A style past the enum is refused by the Projectile itself.
    private static Projectile? ReadProjectile(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 11 ? new Projectile(U32(bytes, 1), U32(bytes, 5), bytes[9], (CombatStyle)bytes[10]) : null;

    private static PrayerView? ReadPrayerView(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 6 && bytes.Length == 6 + bytes[5]
            ? new PrayerView(U16(bytes, 1), U16(bytes, 3), Array.ConvertAll(bytes[6..].ToArray(), id => (int)id))
            : null;

    /// <summary>Reads what <see cref="Line"/> wrote.</summary>
    private static ClientMessage? ReadPair(ReadOnlySpan<byte> bytes, Func<int, long, ClientMessage> make) =>
        bytes.Length == 7 ? make(U16(bytes, 1), U32(bytes, 3)) : null;

    private static Deposit? ReadDeposit(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 8 ? new Deposit(U16(bytes, 1), U32(bytes, 3), bytes[7]) : null;

    // A container past the enum is refused by the MoveItem itself.
    private static MoveItem? ReadMoveItem(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 7 ? new MoveItem((ContainerId)bytes[1], U16(bytes, 3), (ContainerId)bytes[2], U16(bytes, 5)) : null;

    private static ShopView? ReadShopView(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes.Length != 4 + bytes[3] * 6)
        {
            return null;
        }

        var lines = new List<ShopStock>();
        for (var at = 4; at < bytes.Length; at += 6)
        {
            lines.Add(new ShopStock(U16(bytes, at), U32(bytes, at + 2)));
        }

        return new ShopView(U16(bytes, 1), lines);
    }

    private static TradeView? ReadTradeView(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes.Length < 2 + bytes[1] + 3)
        {
            return null;
        }

        var at = 2 + bytes[1];
        int mine = bytes[at + 1];
        int theirs = bytes[at + 2];
        if (bytes.Length != at + 3 + (mine + theirs) * 6)
        {
            return null;
        }

        var stacks = new List<ItemStack>();
        for (var i = 0; i < mine + theirs; i++)
        {
            stacks.Add(new ItemStack(U16(bytes, at + 3 + i * 6), Wire.Narrow(U32(bytes, at + 5 + i * 6))));
        }

        var flags = bytes[at];
        return new TradeView(
            Wire.DecodeText(bytes[2..at]),
            stacks[..mine],
            stacks[mine..],
            new TradeStep((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0));
    }

    private static ConversationChoices? ReadChoices(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
        {
            return null;
        }

        var options = new List<string>();
        var at = 2;
        for (var i = 0; i < bytes[1]; i++)
        {
            if (TextAt(bytes, at) is not { } option)
            {
                return null;
            }

            options.Add(option.Text);
            at = option.Next;
        }

        return at == bytes.Length ? new ConversationChoices(options) : null;
    }

    private static SocialLists? ReadSocialLists(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 3)
        {
            return null;
        }

        var friends = new List<Friend>();
        var at = 2;
        for (var i = 0; i < bytes[1]; i++)
        {
            if (TextAt(bytes, at) is not { } name || name.Next >= bytes.Length)
            {
                return null;
            }

            friends.Add(new Friend(name.Text, bytes[name.Next] == 1));
            at = name.Next + 1;
        }

        if (at >= bytes.Length)
        {
            return null;
        }

        var ignored = new List<string>();
        int count = bytes[at++];
        for (var i = 0; i < count; i++)
        {
            if (TextAt(bytes, at) is not { } name)
            {
                return null;
            }

            ignored.Add(name.Text);
            at = name.Next;
        }

        return at == bytes.Length ? new SocialLists(friends, ignored) : null;
    }

    private static class ClientTag
    {
        public const byte Login = 1;
        public const byte Walk = 2;
        public const byte Interact = 3;
        public const byte Chat = 4;
        public const byte Whisper = 5;
        public const byte EditList = 6;
        public const byte Buy = 7;
        public const byte Sell = 8;
        public const byte Deposit = 9;
        public const byte Withdraw = 10;
        public const byte Continue = 11;
        public const byte Choose = 12;
        public const byte TradeOffer = 13;
        public const byte TradeAccept = 14;
        public const byte TradeDecline = 15;
        public const byte Cast = 16;
        public const byte TogglePrayer = 17;
        public const byte MoveItem = 18;
    }

    private static class ServerTag
    {
        public const byte LoginOk = 1;
        public const byte PrivateState = 2;
        public const byte Notice = 3;
        public const byte Avatar = 4;
        public const byte Said = 5;
        public const byte Whispered = 6;
        public const byte SocialLists = 7;
        public const byte Hit = 8;
        public const byte ShopView = 9;
        public const byte BankOpen = 10;
        public const byte WindowClosed = 11;
        public const byte ConversationLine = 12;
        public const byte ConversationChoices = 13;
        public const byte TradeView = 14;
        public const byte Projectile = 15;
        public const byte PrayerView = 16;
    }
}
