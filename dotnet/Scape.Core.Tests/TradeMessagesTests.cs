using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class TradeMessagesTests
{
    // From a client.

    [Fact]
    public void ABuyAndASellKeepTheItemAndTheCount()
    {
        var buy = RoundTrip(new Buy(300, 70000));
        var sell = RoundTrip(new Sell(ItemDef.MaxId, ItemStack.MaxCount));

        Assert.Equal((300, 70000L), (buy.Item, buy.Count));
        Assert.Equal((ItemDef.MaxId, (long)ItemStack.MaxCount), (sell.Item, sell.Count));
    }

    [Fact]
    public void ABuyAndASellHaveTheirOwnTags()
    {
        Assert.NotEqual(MessageCodec.Encode(new Buy(1, 1))[0], MessageCodec.Encode(new Sell(1, 1))[0]);
    }

    [Fact]
    public void ADepositKeepsTheSlotTheCountAndTheTab()
    {
        var back = RoundTrip(new Deposit(27, 5, 7));

        Assert.Equal((27, 5L, 7), (back.Slot, back.Count, back.Tab));
    }

    [Fact]
    public void AWithdrawalKeepsTheSlotAndTheCount()
    {
        var back = RoundTrip(new Withdraw(799, ItemStack.MaxCount));

        Assert.Equal((799, (long)ItemStack.MaxCount), (back.Slot, back.Count));
    }

    [Fact]
    public void ATradeCannotBeBuiltWithACountOfNothingOrAnItemThatIsNone()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Buy(1, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Sell(0, 1));
        Assert.ThrowsAny<ArgumentException>(() => new Deposit(0, 0, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Withdraw(0x10000, 1));
    }

    [Fact]
    public void BytesWithACountOfZeroAreMalformedNotACrash()
    {
        Assert.Equal(BadMessage.Malformed, WhyClient([7, 1, 0, 0, 0, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([9, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void TradeBytesOfTheWrongLengthAreMalformed()
    {
        Assert.Equal(BadMessage.Malformed, WhyClient([7, 1, 0, 1, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([10, 1, 0, 1, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([9, 1, 0, 1, 0, 0, 0]));
    }

    [Fact]
    public void ATradeOverItsCapIsRefusedBeforeItIsRead()
    {
        Assert.Equal(BadMessage.TooLarge, WhyClient([8, 1, 0, 1, 0, 0, 0, 0]));
    }

    // From the server.

    [Fact]
    public void AShopViewKeepsTheShopAndEveryLine()
    {
        var back = Heard(new ShopView(4, [new ShopStock(2, 10), new ShopStock(9, ShopLine.MaxStock)]));

        Assert.Equal(4, back.Npc);
        Assert.Equal([new ShopStock(2, 10), new ShopStock(9, ShopLine.MaxStock)], back.Lines);
    }

    [Fact]
    public void AShopViewMayHaveNoLinesAndMayFillTheCap()
    {
        Assert.Empty(Heard(new ShopView(1, [])).Lines);
        Assert.Equal(ShopDef.MaxLines, Heard(new ShopView(1, [.. Enumerable.Range(1, ShopDef.MaxLines).Select(i => new ShopStock(i, i))])).Lines.Count);
    }

    [Fact]
    public void AShopViewWithTooManyLinesCannotBeBuilt()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShopView(1, [.. Enumerable.Repeat(new ShopStock(1, 1), ShopDef.MaxLines + 1)]));
    }

    [Fact]
    public void AShopViewWhoseLengthByteLiesIsMalformed()
    {
        Assert.Equal(BadMessage.Malformed, WhyServer([9, 1, 0, 2, 1, 0, 1, 0, 0, 0]));
    }

    [Fact]
    public void BankOpenAndWindowClosedAreOneByteEach()
    {
        Assert.Single(MessageCodec.Encode(new BankOpen()));
        Assert.Single(MessageCodec.Encode(new WindowClosed()));
        _ = Heard(new BankOpen());
        _ = Heard(new WindowClosed());
        Assert.Equal(BadMessage.TooLarge, WhyServer([10, 0]));
    }
}
