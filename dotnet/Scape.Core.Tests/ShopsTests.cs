using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class ShopsTests
{
    private static readonly ItemSet Stock = Items();

    private static readonly ShopRules Rules = new(Coins);

    // Prices.

    [Fact]
    public void AShopSellsAboveTheValueAndPaysBelowIt()
    {
        var item = Stock.Require(Logs);

        Assert.Equal(5, Rules.BuyPrice(item));
        Assert.Equal(1, Rules.SellPrice(item));
    }

    [Fact]
    public void AWorthlessItemStillCostsACoin()
    {
        var item = Stock.Require(Helmet);

        Assert.Equal(1, Rules.BuyPrice(item));
        Assert.Equal(1, Rules.SellPrice(item));
    }

    [Fact]
    public void ASaleNeverPaysMoreThanThePurchaseCost()
    {
        foreach (var item in Stock.Items)
        {
            Assert.True(Rules.SellPrice(item) <= Rules.BuyPrice(item));
        }
    }

    [Fact]
    public void RefusesToPayMoreThanItSellsFor()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShopRules(Coins, buyPercent: 50, sellPercent: 60));
        Assert.ThrowsAny<ArgumentException>(() => new ShopRules(0));
        Assert.ThrowsAny<ArgumentException>(() => new ShopRules(Coins, sellPercent: 0));
    }

    // A shop.

    [Fact]
    public void FindsALineByItem()
    {
        var shop = new ShopDef([new ShopLine(Logs, 10)]);

        Assert.Equal(10, shop.Find(Logs)?.Cap);
        Assert.Null(shop.Find(Sword));
    }

    [Fact]
    public void RefusesNoLinesTooManyAndARepeatedItem()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShopDef([]));
        Assert.ThrowsAny<ArgumentException>(() => new ShopDef([new ShopLine(Logs, 1), new ShopLine(Logs, 2)]));
        Assert.ThrowsAny<ArgumentException>(() => new ShopDef([.. Enumerable.Range(1, ShopDef.MaxLines + 1).Select(i => new ShopLine(i, 1))]));
    }

    [Fact]
    public void ALineStartsFullUnlessItSaysOtherwise()
    {
        Assert.Equal(8, new ShopLine(Logs, 8).Start);
        Assert.Equal(0, new ShopLine(Logs, 8, start: 0).Start);
    }

    [Fact]
    public void RefusesALineWithNoRoomNoCapNoRestockOrABadStart()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(0, 1));
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(Logs, 0));
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(Logs, ShopLine.MaxStock + 1));
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(Logs, 1, restockTicks: 0));
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(Logs, 2, start: 3));
        Assert.ThrowsAny<ArgumentException>(() => new ShopLine(Logs, 2, start: -1));
    }
}
