using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class MessagesTests
{
    // Names and text.

    [Fact]
    public void ANameIsLettersDigitsAndInnerSpacesUpToTwelve()
    {
        foreach (var name in new[] { "a", "Ana", "Zed 99", "a_b-c", "abcdefghijkl" })
        {
            Assert.True(Wire.IsValidName(name), name);
        }
    }

    [Fact]
    public void AnEmptyLongPaddedOrOddNameIsRefused()
    {
        foreach (var name in new[] { string.Empty, "abcdefghijklm", " ana", "ana ", "_ana", "aé", "a\nb" })
        {
            Assert.False(Wire.IsValidName(name), name);
        }
    }

    [Fact]
    public void NamesThatDifferOnlyInCaseShareAKey()
    {
        Assert.Equal(Wire.NameKey("Ana"), Wire.NameKey("aNA"));
    }

    [Fact]
    public void TextMayBeAnythingPrintableUpTo240Bytes()
    {
        Assert.True(Wire.IsValidText("hello é world"));
        Assert.True(Wire.IsValidText(new string('x', 240)));
        Assert.False(Wire.IsValidText(new string('x', 241)));
        Assert.False(Wire.IsValidText(new string('é', 121)));
        Assert.False(Wire.IsValidText(string.Empty));
        Assert.False(Wire.IsValidText("a\tb"));
        Assert.False(Wire.IsValidText("a\u007fb"));
    }

    // Messages from a client.

    [Fact]
    public void ALoginRoundTrips()
    {
        Assert.Equal("Ana", RoundTrip(new Login("Ana")).Name);
    }

    [Fact]
    public void ALoginCarriesItsPasswordAndWhetherToCreate()
    {
        var back = RoundTrip(new Login("Ana", password: "pässword1", create: true));

        Assert.Equal("Ana", back.Name);
        Assert.Equal("pässword1", back.Password);
        Assert.True(back.Create);
    }

    [Fact]
    public void ALoginWithNoPasswordRoundTripsAsOneThatDoesNotCreate()
    {
        var back = RoundTrip(new Login("Ana"));

        Assert.Equal(string.Empty, back.Password);
        Assert.False(back.Create);
    }

    [Fact]
    public void ALoginPasswordMayBeAsLongAsTheLimitAndNoLonger()
    {
        var longest = new string('x', Wire.MaxPasswordBytes);

        Assert.Equal(longest, RoundTrip(new Login("Ana", password: longest)).Password);
        Assert.ThrowsAny<ArgumentException>(() => new Login("Ana", password: longest + "x"));
        Assert.ThrowsAny<ArgumentException>(() => new Login("Ana", password: new string('é', (Wire.MaxPasswordBytes / 2) + 1)));
    }

    [Fact]
    public void ALoginThatIsCutShortOrHasAStrayByteIsMalformed()
    {
        var good = MessageCodec.Encode(new Login("Ana", password: "abcdefgh"));

        foreach (var cut in new[] { 1, 2, 4, 5, 6, good.Length - 1 })
        {
            Assert.NotNull(WhyClient(good[..cut]));
        }

        Assert.Equal(BadMessage.Malformed, WhyClient([.. good, 0]));
        var flagged = good.ToArray();
        flagged[5] = 2;
        Assert.Equal(BadMessage.Malformed, WhyClient(flagged));
    }

    [Fact]
    public void ALoginInTheOldLayoutIsMalformed()
    {
        Assert.Equal(BadMessage.Malformed, WhyClient([1, 3, 65, 110, 97]));
    }

    [Fact]
    public void AWalkRoundTripsWithEveryTileField()
    {
        var target = new TileCoord(TileWorld.WorldSize - 1, 12345, TileWorld.PlaneCount - 1);

        Assert.Equal(target, RoundTrip(new Walk(target)).Target);
    }

    [Fact]
    public void AnInteractionRoundTrips()
    {
        var back = RoundTrip(new Interact(255, 0xFFFFFFFF, 7));

        Assert.Equal(255, back.Kind);
        Assert.Equal(0xFFFFFFFFL, back.Id);
        Assert.Equal(7, back.Option);
    }

    [Fact]
    public void AChatLineRoundTripsWithItsAccents()
    {
        Assert.Equal("café ☃", RoundTrip(new Chat("café ☃")).Text);
    }

    [Fact]
    public void AMessageCannotBeBuiltFromValuesThatAreOutOfRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Login("no way too long"));
        Assert.ThrowsAny<ArgumentException>(() => new Chat(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => new Interact(256, 0, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Interact(0, -1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Interact(0, 0, 256));
        Assert.ThrowsAny<ArgumentException>(() => new Interact(0, 0x100000000, 0));
    }

    [Fact]
    public void AClientMessageSaysWhyItsBytesAreUnreadable()
    {
        Assert.Equal(BadMessage.Empty, WhyClient([]));
        Assert.Equal(BadMessage.UnknownTag, WhyClient([99, 1, 2]));
        Assert.Equal(BadMessage.UnknownTag, WhyClient([0]));

        // A walk is six bytes.
        Assert.Equal(BadMessage.Malformed, WhyClient([2, 0, 0]));
        Assert.Equal(BadMessage.TooLarge, WhyClient([2, 0, 0, 0, 0, 0, 0]));

        // A login whose length byte runs past the end.
        Assert.Equal(BadMessage.Malformed, WhyClient([1, 5, 65]));

        // A tile outside the world, and a plane that does not exist.
        Assert.Equal(BadMessage.Malformed, WhyClient([2, 0xFF, 0xFF, 0, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([2, 0, 0, 0, 0, TileWorld.PlaneCount]));

        // A login name that is not a name.
        Assert.Equal(BadMessage.Malformed, WhyClient([1, 2, 0x20, 0x20]));

        // A chat line that is not UTF-8, and one with a control character.
        Assert.Equal(BadMessage.Malformed, WhyClient([4, 2, 0xC3, 0x28]));
        Assert.Equal(BadMessage.Malformed, WhyClient([4, 1, 0x07]));
    }

    [Fact]
    public void AMessageOverItsOwnCapIsTooLarge()
    {
        var chat = new byte[2 + 241];
        chat[0] = 4;
        chat[1] = 241;

        Assert.Equal(BadMessage.TooLarge, WhyClient(chat));
        Assert.Equal(BadMessage.TooLarge, WhyClient([1, .. Enumerable.Repeat((byte)65, 80)]));
    }

    [Fact]
    public void NoMessageFromAClientIsOverTheStatedLimit()
    {
        Assert.True(MessageCodec.Encode(new Chat(new string('x', 240))).Length <= MessageCodec.ClientMessageLimit);
    }

    // C# only: UTF-8 in .NET keeps a leading byte order mark, and the Dart reader drops one.
    [Fact]
    public void OneLeadingByteOrderMarkIsDroppedFromText()
    {
        Assert.Equal("hi", Assert.IsType<Chat>(Read([4, 5, 0xEF, 0xBB, 0xBF, 0x68, 0x69])).Text);
        Assert.Equal("﻿hi", Assert.IsType<Chat>(Read([4, 8, 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x68, 0x69])).Text);
        Assert.Equal("Ana", Assert.IsType<Login>(Read([1, 6, 0xEF, 0xBB, 0xBF, 65, 110, 97, 0, 0])).Name);

        static ClientMessage Read(byte[] bytes) =>
            Assert.IsType<Understood<ClientMessage>>(MessageCodec.DecodeClient(bytes)).Message;
    }

    // Messages from the server.

    [Fact]
    public void ALoginOkRoundTrips()
    {
        Assert.Equal("ana", Heard(new LoginOk("ana")).Name);
    }

    [Fact]
    public void AnAvatarRoundTripsTheLargestId()
    {
        Assert.Equal(0xFFFFFFFFL, Heard(new Avatar(0xFFFFFFFF)).NetworkId);
        Assert.ThrowsAny<ArgumentException>(() => new Avatar(-1));
        Assert.ThrowsAny<ArgumentException>(() => new Avatar(0x100000000));
        Assert.Equal(BadMessage.Malformed, WhyServer([4, 1, 2]));
    }

    [Fact]
    public void PrivateStateKeepsItsBytes()
    {
        byte[] delta = [0, 1, 2, 255];

        Assert.Equal(delta, Heard(new PrivateState(delta)).Delta.ToArray());
    }

    [Fact]
    public void ANoticeRoundTrips()
    {
        Assert.Equal("Welcome", Heard(new Notice("Welcome")).Text);
    }

    [Fact]
    public void PrivateStateOverItsCapCannotBeBuiltOrRead()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PrivateState(new byte[Wire.MaxDeltaBytes + 1]));
        Assert.Equal(BadMessage.TooLarge, WhyServer([2, .. new byte[Wire.MaxDeltaBytes + 1]]));
        Assert.Equal(Wire.MaxDeltaBytes, new PrivateState(new byte[Wire.MaxDeltaBytes]).Delta.Length);
    }

    [Fact]
    public void AServerMessageSaysWhyItsBytesAreUnreadable()
    {
        Assert.Equal(BadMessage.Empty, WhyServer([]));
        Assert.Equal(BadMessage.UnknownTag, WhyServer([77]));
        Assert.Equal(BadMessage.Malformed, WhyServer([3, 9, 65]));
    }

    // A login refusal.

    [Fact]
    public void ALoginRefusalIsToldToTheClientByItsName()
    {
        // The names are the wire, so they are written out rather than worked out.
        string[] names = ["nameTaken", "unreadable", "badLogin", "weakPassword", "accountExists", "busy", "unavailable", "banned"];

        Assert.Equal(Enum.GetValues<LoginRefusal>().Select(refusal => (LoginRefusal?)refusal), names.Select(name => LoginRefusal.FromWire(name)));
        Assert.Null(LoginRefusal.FromWire("something else"));
    }

    // Random bytes.

    [Fact]
    public void RandomBytesNeverThrowPastEitherDecoder()
    {
        var random = new Random(7);
        for (var i = 0; i < 20000; i++)
        {
            var bytes = new byte[random.Next(i % 2 == 0 ? 12 : 300)];
            random.NextBytes(bytes);
            if (bytes.Length > 0 && i % 3 == 0)
            {
                bytes[0] = (byte)(1 + random.Next(7));
            }

            _ = MessageCodec.DecodeClient(bytes);
            _ = MessageCodec.DecodeServer(bytes);
        }
    }
}
