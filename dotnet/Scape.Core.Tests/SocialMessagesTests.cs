using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class SocialMessagesTests
{
    // From a client.

    [Fact]
    public void AWhisperKeepsWhoItIsForAndWhatItSays()
    {
        var back = RoundTrip(new Whisper("Bo", "café ☃"));

        Assert.Equal("Bo", back.To);
        Assert.Equal("café ☃", back.Text);
    }

    [Fact]
    public void AListEditKeepsTheListTheDirectionAndTheName()
    {
        foreach (var list in Enum.GetValues<ListKind>())
        {
            foreach (var add in new[] { true, false })
            {
                var back = RoundTrip(new EditList(list, add, "Bo"));

                Assert.Equal((list, add, "Bo"), (back.List, back.Add, back.Name));
            }
        }
    }

    [Fact]
    public void ASocialMessageCannotBeBuiltFromABadNameOrText()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Whisper("no way too long", "hi"));
        Assert.ThrowsAny<ArgumentException>(() => new Whisper("Bo", string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => new Whisper("Bo", new string('x', 241)));
        Assert.ThrowsAny<ArgumentException>(() => new EditList(ListKind.Friends, true, " "));
    }

    // C# only: a cast can make a list that Dart's enum cannot hold.
    [Fact]
    public void AListEditCannotBeBuiltForAListOutsideTheEnum()
    {
        Assert.ThrowsAny<ArgumentException>(() => new EditList((ListKind)9, true, "Bo"));
    }

    [Fact]
    public void AClientSaysWhyAWhisperOrAListEditIsUnreadable()
    {
        // The first text runs past the end.
        Assert.Equal(BadMessage.Malformed, WhyClient([5, 9, 66]));

        // No second text.
        Assert.Equal(BadMessage.Malformed, WhyClient([5, 2, 66, 111]));

        // Bytes left over after the second text.
        Assert.Equal(BadMessage.Malformed, WhyClient([5, 1, 66, 1, 104, 0]));

        // A name that is not a name.
        Assert.Equal(BadMessage.Malformed, WhyClient([5, 1, 0x20, 1, 104]));

        // Unused flag bits, and a length that does not match.
        Assert.Equal(BadMessage.Malformed, WhyClient([6, 4, 1, 66]));
        Assert.Equal(BadMessage.Malformed, WhyClient([6, 0, 3, 66]));
        Assert.Equal(BadMessage.Malformed, WhyClient([6]));
    }

    [Fact]
    public void AWhisperAtTheTextCapIsLetThroughAndOneOverIsNot()
    {
        var fits = MessageCodec.Encode(new Whisper("abcdefghijkl", new string('x', Wire.MaxTextBytes)));

        Assert.True(fits.Length <= MessageCodec.ClientMessageLimit);
        Assert.Equal(BadMessage.TooLarge, WhyClient([.. fits, 0]));
    }

    // From the server.

    [Fact]
    public void SaidKeepsTheSpeakerAndTheLine()
    {
        var back = Heard(new Said(0xFFFFFFFF, "hello there"));

        Assert.Equal((0xFFFFFFFFL, "hello there"), (back.Speaker, back.Text));
    }

    [Fact]
    public void WhisperedKeepsWhoFromAndTheLine()
    {
        var back = Heard(new Whispered("Ana", "psst"));

        Assert.Equal(("Ana", "psst"), (back.From, back.Text));
    }

    [Fact]
    public void SocialListsKeepFriendsWhoIsOnlineAndTheIgnored()
    {
        var back = Heard(new SocialLists([new Friend("Bo", true), new Friend("Cy", false)], ["Dee"]));

        Assert.Equal([new Friend("Bo", true), new Friend("Cy", false)], back.Friends);
        Assert.Equal(["Dee"], back.Ignored);
    }

    [Fact]
    public void EmptySocialListsRoundTrip()
    {
        var back = Heard(new SocialLists([], []));

        Assert.Empty(back.Friends);
        Assert.Empty(back.Ignored);
    }

    [Fact]
    public void FullSocialListsFitTheStatedCap()
    {
        string[] names = [.. Enumerable.Range(0, Wire.MaxListEntries).Select(i => ("abcdefghi" + (i % 10) + (i / 10) + "x")[..12])];
        var lists = new SocialLists([.. names.Select(name => new Friend(name, true))], names);

        Assert.Equal(Wire.MaxListEntries, Heard(lists).Friends.Count);
    }

    [Fact]
    public void SocialListsCannotBeBuiltOverTheListCapOrFromABadName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SocialLists([.. Enumerable.Repeat(new Friend("a", false), Wire.MaxListEntries + 1)], []));
        Assert.ThrowsAny<ArgumentException>(() => new SocialLists([], ["_x"]));
        Assert.ThrowsAny<ArgumentException>(() => new Said(-1, "x"));
    }

    [Fact]
    public void TheServerSaysWhySocialBytesAreUnreadable()
    {
        Assert.Equal(BadMessage.Malformed, WhyServer([5, 1, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyServer([6, 1, 66]));

        // The lists run out part way.
        Assert.Equal(BadMessage.Malformed, WhyServer([7, 1, 2, 66]));
        Assert.Equal(BadMessage.Malformed, WhyServer([7, 0]));
        Assert.Equal(BadMessage.Malformed, WhyServer([7, 0, 1]));
        Assert.Equal(BadMessage.Malformed, WhyServer([7, 0, 0, 0]));
    }
}
