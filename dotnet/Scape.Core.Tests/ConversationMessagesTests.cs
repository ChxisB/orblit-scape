using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class ConversationMessagesTests
{
    [Fact]
    public void ALineKeepsItsSpeakerAndText()
    {
        var back = Heard(new ConversationLine("Brindle", "Hello."));

        Assert.Equal(("Brindle", "Hello."), (back.Speaker, back.Text));
    }

    [Fact]
    public void ChoicesKeepTheirOrder()
    {
        Assert.Equal(["Yes.", "No.", "Maybe."], Heard(new ConversationChoices(["Yes.", "No.", "Maybe."])).Options);
    }

    [Fact]
    public void ALineAndChoicesAtTheirLargestStillFitTheirCaps()
    {
        var longest = new string('a', Wire.MaxTextBytes);

        Assert.Equal(longest, Heard(new ConversationLine(longest, longest)).Text);
        Assert.Equal(Wire.MaxChoices, Heard(new ConversationChoices([.. Enumerable.Repeat(longest, Wire.MaxChoices)])).Options.Count);
    }

    [Fact]
    public void ConversationMessagesCannotBeBuiltEmptyOverTheLimitOrWithBadText()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ConversationChoices([]));
        Assert.ThrowsAny<ArgumentException>(() => new ConversationChoices([.. Enumerable.Repeat("x", Wire.MaxChoices + 1)]));
        Assert.ThrowsAny<ArgumentException>(() => new ConversationChoices([string.Empty]));
        Assert.ThrowsAny<ArgumentException>(() => new ConversationLine(string.Empty, "x"));
        Assert.ThrowsAny<ArgumentException>(() => new ConversationLine("x", "a\nb"));
    }

    [Fact]
    public void TheServerRefusesBytesThatDoNotMatchTheirLengths()
    {
        Assert.Equal(BadMessage.Malformed, WhyServer([12, 3, 65]));
        Assert.Equal(BadMessage.Malformed, WhyServer([13, 2, 1, 65]));
        Assert.Equal(BadMessage.Malformed, WhyServer([13, 1, 1, 65, 0]));
        Assert.Equal(BadMessage.Malformed, WhyServer([13, 0]));
    }

    [Fact]
    public void ContinueAndChooseRoundTripAndRefuseExtraBytes()
    {
        _ = RoundTrip(new Continue());
        Assert.Equal(255, RoundTrip(new Choose(255)).Index);
        Assert.ThrowsAny<ArgumentException>(() => new Choose(256));
        Assert.Equal(BadMessage.TooLarge, WhyClient([11, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([12]));
    }
}
