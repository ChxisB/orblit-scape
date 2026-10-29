using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class TradesTests : IAsyncDisposable
{
    private readonly List<string> _logged = [];
    private TestServer _test;

    public TradesTests() => _test = new TestServer(options => options with { Log = _logged.Add });

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private Container Bag(string player) => Server.Items.ContainerOf(player, ContainerId.Inventory)!;

    private long IdOf(string player) => (long)Server.Movement.Standing(player)!.Value.NetworkId;

    private async Task<TestClient> JoinAsync(string name)
    {
        var client = await _test.JoinAsync(name);
        await _test.TickAsync();
        return client;
    }

    /// <summary>Joins Ana and Bo and stands them next to each other.</summary>
    private async Task<(TestClient Ana, TestClient Bo)> PairAsync()
    {
        var ana = await JoinAsync("Ana");
        var bo = await JoinAsync("Bo");
        await bo.SendAsync(new Walk(At(33, 32)));
        await _test.TickAsync(3);
        return (ana, bo);
    }

    private async Task ClickAsync(TestClient client, string other)
    {
        await client.ClickAsync(InteractKind.Player, IdOf(other));
        await _test.TickAsync(2);
    }

    /// <summary>Both players ask for the trade, and it opens.</summary>
    private async Task OpenAsync(TestClient ana, TestClient bo)
    {
        await ClickAsync(ana, "bo");
        await ClickAsync(bo, "ana");
    }

    private async Task<(TestClient Ana, TestClient Bo)> TradingAsync()
    {
        var (ana, bo) = await PairAsync();
        await OpenAsync(ana, bo);
        return (ana, bo);
    }

    private static Task OfferAsync(TestClient client, int item, long count) => client.SendAsync(new TradeOffer(item, count));

    /// <summary>Both players accept the offer step and then the confirm step.</summary>
    private async Task AcceptTwiceAsync(TestClient ana, TestClient bo)
    {
        for (var step = 0; step < 2; step++)
        {
            await ana.SendAsync(new TradeAccept());
            await bo.SendAsync(new TradeAccept());
            await _test.TickAsync();
        }
    }

    // A request.

    [Fact]
    public async Task ARequestIsToldToBothAndOpensTheTradeWhenReturned()
    {
        var (ana, bo) = await PairAsync();

        await ClickAsync(ana, "bo");
        Assert.Contains("Sending a trade request to bo.", ana.Notices);
        Assert.Contains("ana wants to trade with you.", bo.Notices);
        Assert.Empty(ana.Views);
        await ClickAsync(bo, "ana");

        Assert.Equal("bo", ana.Views[^1].Partner);
        Assert.Equal("ana", bo.Views[^1].Partner);
    }

    [Fact]
    public async Task ARequestIsDroppedAfterAMinute()
    {
        var (ana, bo) = await PairAsync();
        await ana.ClickAsync(InteractKind.Player, IdOf("bo"));
        await _test.TickAsync(105);

        await ClickAsync(bo, "ana");

        Assert.Empty(ana.Views);
        Assert.Contains("Sending a trade request to ana.", bo.Notices);
    }

    [Fact]
    public async Task ARequestIsNeverToldToAPlayerWhoIgnoresTheSender()
    {
        var (ana, bo) = await PairAsync();
        await bo.SendAsync(new EditList(ListKind.Ignored, add: true, "ana"));

        await OpenAsync(ana, bo);

        Assert.Contains("Sending a trade request to bo.", ana.Notices);
        Assert.DoesNotContain("ana wants to trade with you.", bo.Notices);
        Assert.Empty(ana.Views);
    }

    [Fact]
    public async Task ARequestIsNotToldTwiceToThePlayerItNames()
    {
        var (ana, bo) = await PairAsync();

        await ClickAsync(ana, "bo");
        await ClickAsync(ana, "bo");

        Assert.Single(bo.Notices, "ana wants to trade with you.");
    }

    [Fact]
    public async Task ARequestDoesNotOpenForAPlayerWhoIgnoresTheSenderByNow()
    {
        var (ana, bo) = await PairAsync();
        await ClickAsync(ana, "bo");
        await ana.SendAsync(new EditList(ListKind.Ignored, add: true, "bo"));

        await ClickAsync(bo, "ana");

        Assert.Empty(ana.Views);
        Assert.Empty(bo.Views);
    }

    [Fact]
    public async Task ARequestIsRefusedForAPlayerAlreadyInATrade()
    {
        var (ana, bo) = await PairAsync();
        var cy = await JoinAsync("Cy");
        await cy.SendAsync(new Walk(At(31, 32)));
        await _test.TickAsync(3);
        await OpenAsync(ana, bo);

        await ClickAsync(cy, "ana");

        Assert.Contains("That player is busy.", cy.Notices);
    }

    [Fact]
    public async Task ARequestFromAPlayerInATradeIsRefused()
    {
        var (ana, bo) = await PairAsync();
        var cy = await JoinAsync("Cy");
        await cy.SendAsync(new Walk(At(31, 32)));
        await _test.TickAsync(3);
        await OpenAsync(ana, bo);

        await ClickAsync(ana, "cy");

        Assert.Contains("You are busy.", ana.Notices);
        Assert.DoesNotContain("ana wants to trade with you.", cy.Notices);
    }

    [Fact]
    public async Task ARequestIsRefusedForAPlayerWithAnotherWindowOpen()
    {
        var (ana, bo) = await PairAsync();
        var booth = Server.Objects.Place(ObjectId.BankBooth, At(34, 32));
        await bo.ClickAsync(InteractKind.PlacedObject, (long)booth.NetworkId);
        await _test.TickAsync(2);

        await ClickAsync(ana, "bo");

        Assert.Contains(bo.Messages, message => message is BankOpen);
        Assert.Contains("That player is busy.", ana.Notices);
    }

    [Fact]
    public async Task ARequestIsAskedAgainOnceATradeEnds()
    {
        var (ana, bo) = await TradingAsync();
        await bo.SendAsync(new TradeDecline());
        await _test.TickAsync();
        var views = ana.Views.Count;

        await ClickAsync(bo, "ana");

        Assert.Contains("bo wants to trade with you.", ana.Notices);
        Assert.Equal(views, ana.Views.Count);
    }

    [Fact]
    public async Task ARequestGoesWhenThePlayerWhoMadeItLeaves()
    {
        var (ana, bo) = await PairAsync();
        await ClickAsync(ana, "bo");
        await ana.Link.CloseAsync();
        await _test.TickAsync(12);
        ana = await JoinAsync("Ana");

        await ClickAsync(bo, "ana");

        Assert.Contains("bo wants to trade with you.", ana.Notices);
        Assert.Empty(ana.Views);
    }

    [Fact]
    public async Task AClickOnOneselfAsksNothing()
    {
        var ana = await JoinAsync("Ana");

        await ClickAsync(ana, "ana");

        Assert.Empty(ana.Notices);
    }

    /// <summary>
    /// Ana asks Bo. Bo walks off east and clicks back while Ana walks <paramref name="steps"/>
    /// tiles west, so Bo reaches the tile beside where Ana stood and finds Ana further away.
    /// </summary>
    private async Task<(TestClient Ana, TestClient Bo)> ReturnAsAnaWalksOffAsync(int steps)
    {
        var (ana, bo) = await PairAsync();
        await ClickAsync(ana, "bo");
        await bo.SendAsync(new Walk(At(37, 32)));
        await _test.TickAsync(6);

        await bo.ClickAsync(InteractKind.Player, IdOf("ana"));
        await ana.SendAsync(new Walk(At(32 - steps, 32)));
        await _test.TickAsync(10);
        return (ana, bo);
    }

    [Fact]
    public async Task ATradeOpensWithAPlayerTwoTilesAway()
    {
        var (ana, _) = await ReturnAsAnaWalksOffAsync(steps: 1);

        Assert.Equal("bo", ana.Views[^1].Partner);
    }

    [Fact]
    public async Task ATradeDoesNotOpenWithAPlayerThreeTilesAway()
    {
        var (ana, bo) = await ReturnAsAnaWalksOffAsync(steps: 2);

        Assert.Contains("That player is too far away.", bo.Notices);
        Assert.Empty(ana.Views);
    }

    // A trade.

    [Fact]
    public async Task ATradeShowsEachSideWhatBothHaveOffered()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);

        await OfferAsync(ana, ItemId.Logs, 2);
        await _test.TickAsync();

        Assert.Equal([new ItemStack(ItemId.Logs, 2)], ana.Views[^1].Mine);
        Assert.Equal([new ItemStack(ItemId.Logs, 2)], bo.Views[^1].Theirs);
        Assert.Empty(bo.Views[^1].Mine);
    }

    [Fact]
    public async Task ATradeSwapsWhatWasOfferedOnceBothAcceptTwice()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        Bag("bo").Add(ItemId.Feathers, 2);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 3);
        await OfferAsync(bo, ItemId.Feathers, 2);

        await AcceptTwiceAsync(ana, bo);

        Assert.Equal(0, Bag("ana").CountOf(ItemId.Logs));
        Assert.Equal(2, Bag("ana").CountOf(ItemId.Feathers));
        Assert.Equal(3, Bag("bo").CountOf(ItemId.Logs));
        Assert.Equal(0, Bag("bo").CountOf(ItemId.Feathers));
        Assert.Equal(1, ana.Closes);
        Assert.Equal(1, bo.Closes);
        Assert.Contains("The trade is complete.", ana.Notices);
        Assert.Equal("Trade: ana gave 3 x Logs, bo gave 2 x Feathers", Assert.Single(_logged));
    }

    [Fact]
    public async Task ATradeMovesToTheConfirmStepAfterTheFirstAccepts()
    {
        var (ana, bo) = await TradingAsync();

        await ana.SendAsync(new TradeAccept());
        await bo.SendAsync(new TradeAccept());
        await _test.TickAsync();

        Assert.True(ana.Views[^1].Step.Confirming);
        Assert.False(ana.Views[^1].Step.YouAccepted);
    }

    [Fact]
    public async Task ATradeLosesAnAcceptWhenEitherSideChangesAnOffer()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);
        await ana.SendAsync(new TradeAccept());
        await _test.TickAsync();
        Assert.True(bo.Views[^1].Step.TheyAccepted);

        await OfferAsync(ana, ItemId.Logs, 1);
        await _test.TickAsync();

        Assert.False(bo.Views[^1].Step.TheyAccepted);
        Assert.False(ana.Views[^1].Step.YouAccepted);
    }

    [Fact]
    public async Task ATradeKeepsAnAcceptWhenAnOfferIsRepeated()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 1);
        await bo.SendAsync(new TradeAccept());
        await _test.TickAsync();

        await OfferAsync(ana, ItemId.Logs, 1);
        await OfferAsync(ana, ItemId.Feathers, 0);
        await _test.TickAsync();

        Assert.True(ana.Views[^1].Step.TheyAccepted);
    }

    // The Dart test also checks that no trade journal was written. The journal comes with saves in 3f.
    [Fact]
    public async Task ATradeEndsWhenNothingWasOffered()
    {
        var (ana, bo) = await TradingAsync();

        await AcceptTwiceAsync(ana, bo);

        Assert.Contains("Nothing was traded.", ana.Notices);
        Assert.Equal(1, ana.Closes);
        Assert.Empty(_logged);
    }

    /// <summary>Opens a trade in which Ana offers one each of a bag of different items, 1000 and up.</summary>
    private async Task<TestClient> OfferEveryLineAsync()
    {
        var extra = Enumerable.Range(1000, 30).Select(id => new ItemDef(id, $"Thing {id}", value: 1));
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Items = new ItemSet([.. DefaultItems.All.Items, .. extra], DefaultSkills.All) });
        var (ana, bo) = await PairAsync();
        for (var id = 1000; id < 1000 + Wire.MaxTradeLines; id++)
        {
            Bag("ana").Add(id, 1);
        }

        await OpenAsync(ana, bo);
        for (var id = 1000; id < 1000 + Wire.MaxTradeLines; id++)
        {
            await OfferAsync(ana, id, 1);
            await _test.TickAsync();
        }

        return ana;
    }

    [Fact]
    public async Task ATradeOffersAtMostABagOfDifferentItems()
    {
        var ana = await OfferEveryLineAsync();

        // A player may swap what they hold after offering it, so lines can outgrow the bag.
        Bag("ana").Exchange(take: [new ItemStack(1000, 1)], give: [new ItemStack(1029, 1)]);
        await _test.TickAsync();
        await OfferAsync(ana, 1029, 1);
        await _test.TickAsync();

        Assert.Equal(Wire.MaxTradeLines, ana.Views[^1].Mine.Count);
        Assert.Contains("You cannot offer that many different items.", ana.Notices);
    }

    [Fact]
    public async Task ATradeTakesBackALineWhenTheOfferIsFull()
    {
        var ana = await OfferEveryLineAsync();

        await OfferAsync(ana, 1000, 0);
        await _test.TickAsync();

        Assert.Equal(Wire.MaxTradeLines - 1, ana.Views[^1].Mine.Count);
    }

    [Fact]
    public async Task ATradeGoesThroughWithNothingGivenBack()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 3);

        await AcceptTwiceAsync(ana, bo);

        Assert.Equal(3, Bag("bo").CountOf(ItemId.Logs));
        Assert.Equal("Trade: ana gave 3 x Logs, bo gave nothing", Assert.Single(_logged));
    }

    [Fact]
    public async Task ATradeStaysOpenWhenAPlayerNoLongerHoldsTheirOffer()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 3);
        await _test.TickAsync();
        Bag("ana").Exchange(take: [new ItemStack(ItemId.Logs, 3)], give: []);

        await AcceptTwiceAsync(ana, bo);

        Assert.Contains("The trade cannot go through. You do not have everything offered.", ana.Notices);
        Assert.Contains("The trade cannot go through. ana does not have everything offered.", bo.Notices);
        Assert.Equal(0, ana.Closes);
    }

    [Fact]
    public async Task ATradeRefusesToOfferMoreThanThePlayerHolds()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        await OpenAsync(ana, bo);

        await OfferAsync(ana, ItemId.Logs, 4);
        await _test.TickAsync();

        Assert.Contains("You do not have that many.", ana.Notices);
        Assert.Empty(ana.Views[^1].Mine);
    }

    [Fact]
    public async Task ATradeStaysOpenAndTellsBothWhenABagHasNoRoom()
    {
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 3);
        Bag("bo").Add(ItemId.Bones, Container.InventorySize);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 3);

        await AcceptTwiceAsync(ana, bo);

        Assert.Equal(3, Bag("ana").CountOf(ItemId.Logs));
        Assert.Equal(0, Bag("bo").CountOf(ItemId.Logs));
        Assert.Contains("The trade cannot go through. You do not have room for that.", bo.Notices);
        Assert.Contains("The trade cannot go through. bo does not have room for that.", ana.Notices);
        Assert.False(ana.Views[^1].Step.Confirming);
        Assert.Equal(0, ana.Closes);
    }

    [Fact]
    public async Task ATradeIsCancelledForBothWhenOneWalksAway()
    {
        var (ana, bo) = await TradingAsync();

        await bo.SendAsync(new Walk(At(45, 32)));
        await _test.TickAsync(8);

        Assert.Equal(1, ana.Closes);
        Assert.Equal(1, bo.Closes);
        Assert.Contains("The trade was cancelled.", ana.Notices);
    }

    [Fact]
    public async Task ATradeIsDeclinedByEitherPlayer()
    {
        var (ana, bo) = await TradingAsync();

        await bo.SendAsync(new TradeDecline());
        await _test.TickAsync();

        Assert.Contains("bo declined the trade.", ana.Notices);
        Assert.Equal(1, ana.Closes);
    }

    [Fact]
    public async Task ATradeEndsWhenAPlayerOpensTheBankAndTheBankStaysOpen()
    {
        var (ana, bo) = await TradingAsync();
        var booth = Server.Objects.Place(ObjectId.BankBooth, At(32, 31));

        await ana.ClickAsync(InteractKind.PlacedObject, (long)booth.NetworkId);
        await _test.TickAsync(2);

        Assert.Contains("The trade was cancelled.", bo.Notices);
        Assert.Contains(ana.Messages, message => message is BankOpen);
        Assert.Equal(0, ana.Closes);
    }

    [Fact]
    public async Task ATradeEndsAndTellsThePartnerWhenAPlayerLeaves()
    {
        var (ana, bo) = await TradingAsync();

        await bo.Link.CloseAsync();
        await _test.TickAsync(12);

        Assert.Contains("bo left the trade.", ana.Notices);
        Assert.Equal(1, ana.Closes);
    }
}

/// <summary>What a client was shown of trades.</summary>
file static class TradeReading
{
    extension(TestClient client)
    {
        public IReadOnlyList<TradeView> Views => [.. client.Messages.OfType<TradeView>()];

        public int Closes => client.Messages.Count(message => message is WindowClosed);
    }
}
