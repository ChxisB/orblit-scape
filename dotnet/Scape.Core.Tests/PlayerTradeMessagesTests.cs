using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class PlayerTradeMessagesTests
{
    private static TradeView Full() => new(
        "bo",
        [new ItemStack(5, 3), new ItemStack(9, ItemStack.MaxCount)],
        [.. Enumerable.Range(1, Wire.MaxTradeLines).Select(i => new ItemStack(i, i))],
        new TradeStep(Confirming: true, YouAccepted: true, TheyAccepted: false));

    private static TradeView Lines(int lines) =>
        new("bo", [.. Enumerable.Repeat(new ItemStack(1, 1), lines)], [], default);

    [Fact]
    public void AnOfferAnAcceptAndADeclineRoundTrip()
    {
        var offer = RoundTrip(new TradeOffer(7, 0));

        Assert.Equal((7, 0L), (offer.Item, offer.Count));
        _ = RoundTrip(new TradeAccept());
        _ = RoundTrip(new TradeDecline());
    }

    [Fact]
    public void AnOfferNamesARealItemAndACountTheStackCanHold()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TradeOffer(0, 1));
        Assert.ThrowsAny<ArgumentException>(() => new TradeOffer(1, -1));
        Assert.ThrowsAny<ArgumentException>(() => new TradeOffer(1, ItemStack.MaxCount + 1L));
        Assert.Equal(BadMessage.Malformed, WhyClient([13, 0, 0, 1, 0, 0, 0]));
        Assert.Equal(BadMessage.TooLarge, WhyClient([14, 0]));
    }

    [Fact]
    public void AViewKeepsBothSidesTheStageAndWhoHasAccepted()
    {
        var back = Heard(Full());

        Assert.Equal("bo", back.Partner);
        Assert.Equal([new ItemStack(5, 3), new ItemStack(9, ItemStack.MaxCount)], back.Mine);
        Assert.Equal(Wire.MaxTradeLines, back.Theirs.Count);
        Assert.Equal(new ItemStack(Wire.MaxTradeLines, Wire.MaxTradeLines), back.Theirs[^1]);
        Assert.Equal(new TradeStep(Confirming: true, YouAccepted: true, TheyAccepted: false), back.Step);
    }

    [Fact]
    public void AViewWithTwoEmptySidesIsJustANameAndFlags()
    {
        var back = Heard(new TradeView("bo", [], [], new TradeStep(Confirming: false, YouAccepted: false, TheyAccepted: true)));

        Assert.Empty(back.Mine);
        Assert.True(back.Step.TheyAccepted);
    }

    [Fact]
    public void AViewCannotBeBuiltWithABadNameOrTooManyLines()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TradeView(string.Empty, [], [], default));
        Assert.ThrowsAny<ArgumentException>(() => Lines(Wire.MaxTradeLines + 1));
    }

    [Fact]
    public void TheServerRefusesAViewThatDoesNotMatchItsLengths()
    {
        var good = MessageCodec.Encode(Full());

        Assert.Equal(BadMessage.Malformed, WhyServer([.. good, 0]));
        Assert.Equal(BadMessage.TooLarge, WhyServer([14, .. new byte[5 + Wire.MaxNameChars + (12 * Wire.MaxTradeLines)]]));
        Assert.Equal(BadMessage.Malformed, WhyServer(good[..^1]));
        Assert.Equal(BadMessage.Malformed, WhyServer([14, 9, 98]));
        Assert.Equal(BadMessage.Malformed, WhyServer([14, 2, 98, 111, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyServer([14, 2, 98, 111, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void APlayerCanBeTheTargetOfAnInteraction()
    {
        Assert.Equal(5, InteractKind.Player);
        Assert.Equal(9, new Interact(InteractKind.Player, 9, 0).Id);
    }
}
