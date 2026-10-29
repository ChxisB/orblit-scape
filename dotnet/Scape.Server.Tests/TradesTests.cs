using System.Collections.Concurrent;
using Orblit.Save;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class TradesTests : IAsyncDisposable
{
    // Storage logs off the tick.
    private readonly ConcurrentQueue<string> _logged = [];
    private TestServer _test;

    public TradesTests() => _test = new TestServer(options => options with { Log = _logged.Enqueue });

    private ScapeServer Server => _test.Server;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static DateTimeOffset Year(int year) => new(year, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<byte[]> SaveOfAsync(MemorySaveStore store, string player) =>
        (await store.ReadAsync(StoreKeys.SaveOf(player)))!.Bytes.ToArray();

    /// <summary>Writes the journal of a trade between Ana and <paramref name="other"/>, stamped at the start of <paramref name="year"/>.</summary>
    private static Task JournalAsync(MemorySaveStore store, string other, Dictionary<string, byte[]> states, int year) =>
        store.WriteAsync(StoreKeys.TradeOf("ana", other), new SaveRecord(TradeJournal.Encode(states), new Custom("trade"), Year(year)));

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

    /// <summary>Stops the server, which saves, and starts another on <paramref name="store"/>, as a restart does.</summary>
    private Task StartAsync(ISaveStore store, Func<DateTimeOffset>? clock = null) => StartAsync(Stores.On(store, clock));

    private async Task StartAsync(Storage storage)
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Log = _logged.Enqueue, Storage = storage });
    }

    /// <summary>
    /// Ana holds 3 logs and Bo 2 feathers, both saved. They trade, and <paramref name="store"/>
    /// keeps only <paramref name="kept"/> of the writes that follow the last accept. A new server
    /// then reads both players.
    /// </summary>
    private async Task CrashAsync(int kept, CrashingStore store, Func<DateTimeOffset> clock)
    {
        await StartAsync(store, clock);
        await JoinAsync("Ana");
        await JoinAsync("Bo");
        Bag("ana").Add(ItemId.Logs, 3);
        Bag("bo").Add(ItemId.Feathers, 2);
        var storage = Stores.On(store, clock);
        await StartAsync(storage);
        var (ana, bo) = await TradingAsync();
        await OfferAsync(ana, ItemId.Logs, 3);
        await OfferAsync(bo, ItemId.Feathers, 2);
        await ana.SendAsync(new TradeAccept());
        await bo.SendAsync(new TradeAccept());
        await _test.TickAsync();
        await ana.SendAsync(new TradeAccept());

        // A save queued before the trade lands off the tick, and must not use up the budget.
        await storage.Queue.FlushAsync();
        store.Budget = kept;
        await bo.SendAsync(new TradeAccept());
        await _test.TickAsync();
        await _test.DisposeAsync();
        store.Budget = null;
        await StartAsync(store, clock);
        await JoinAsync("Ana");
        await JoinAsync("Bo");
    }

    /// <summary>A store holding a save for Ana and Bo, with Ana's mining at <paramref name="xp"/>.</summary>
    private async Task<(MemorySaveStore Store, byte[] Ana, byte[] Bo)> SavedAsync(long xp)
    {
        var store = new MemorySaveStore();
        await StartAsync(store);
        await JoinAsync("Ana");
        await JoinAsync("Bo");
        Server.Skills.Grant("ana", "mining", xp);
        await _test.DisposeAsync();
        return (store, await SaveOfAsync(store, "ana"), await SaveOfAsync(store, "bo"));
    }

    /// <summary>Gives Ana 800 more mining in a later visit, so her save is newer than the one <see cref="SavedAsync"/> made.</summary>
    private async Task GrowAsync(ISaveStore store)
    {
        await StartAsync(store);
        await JoinAsync("Ana");
        Server.Skills.Grant("ana", "mining", 800);
        await _test.DisposeAsync();
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

    [Fact]
    public async Task ATradeEndsWithNothingSavedWhenNothingWasOffered()
    {
        var store = new SpyStore(new MemorySaveStore());
        await StartAsync(store);
        var (ana, bo) = await TradingAsync();

        await AcceptTwiceAsync(ana, bo);
        await _test.DisposeAsync();

        Assert.Contains("Nothing was traded.", ana.Notices);
        Assert.Equal(1, ana.Closes);
        Assert.Empty(_logged);
        Assert.DoesNotContain(store.Writes, write => write.Key.StartsWith("t-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATradeLeavesItsJournalEmptyOnceItIsSaved()
    {
        var store = new MemorySaveStore();
        await StartAsync(store);
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 1);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 1);

        await AcceptTwiceAsync(ana, bo);
        await _test.DisposeAsync();

        Assert.True((await store.ReadAsync(StoreKeys.TradeOf("ana", "bo")))!.Bytes.IsEmpty);
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

    // A crash in a trade.

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ACrashInATradeAfterAnyOfItsFourWritesLeavesTheItemsWhole(int kept)
    {
        await CrashAsync(kept, new CrashingStore(new MemorySaveStore()), Stores.Stepping());

        var traded = kept > 0;
        Assert.Equal(traded ? 0 : 3, Bag("ana").CountOf(ItemId.Logs));
        Assert.Equal(traded ? 3 : 0, Bag("bo").CountOf(ItemId.Logs));
        Assert.Equal(traded ? 2 : 0, Bag("ana").CountOf(ItemId.Feathers));
        Assert.Equal(traded ? 0 : 2, Bag("bo").CountOf(ItemId.Feathers));
    }

    [Fact]
    public async Task ACrashInATradeIsFinishedOnceAndNotAgainOnTheNextStart()
    {
        var store = new CrashingStore(new MemorySaveStore());
        var clock = Stores.Stepping();
        await CrashAsync(1, store, clock);
        await _test.DisposeAsync();
        Assert.True((await store.ReadAsync(StoreKeys.TradeOf("ana", "bo")))!.Bytes.IsEmpty);
        Assert.Equal(2, _logged.Count(line => line.Contains("cut short", StringComparison.Ordinal)));
        _logged.Clear();

        await StartAsync(store, clock);
        await JoinAsync("Ana");

        Assert.Empty(_logged);
    }

    // A trade journal.

    [Fact]
    public async Task ATradeJournalBringsASaveOlderThanItUpToIt()
    {
        var (store, older, bo) = await SavedAsync(100);
        await GrowAsync(store);
        await JournalAsync(store, "bo", new() { ["ana"] = older, ["bo"] = bo }, 2100);

        await StartAsync(store);
        await JoinAsync("Ana");

        Assert.Equal(100, Server.Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task ATradeJournalLeavesASaveNewerThanItAlone()
    {
        var (store, older, bo) = await SavedAsync(100);
        await GrowAsync(store);
        await JournalAsync(store, "bo", new() { ["ana"] = older, ["bo"] = bo }, 2020);

        await StartAsync(store);
        await JoinAsync("Ana");

        Assert.Equal(900, Server.Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task ADamagedTradeJournalIsLoggedAndDoesNotStopALogin()
    {
        var store = new MemorySaveStore();
        await store.WriteAsync(StoreKeys.TradeOf("ana", "bo"), new SaveRecord([1, 2, 3], new Custom("trade"), Year(2026)));
        await StartAsync(store);

        var ana = await JoinAsync("Ana");

        Assert.True(ana.Admitted);
        Assert.Contains("is damaged and was left", Assert.Single(_logged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATradeJournalReplayTakesEachPlayerFromTheNewestJournalThatNamesThem()
    {
        var (store, older, bo) = await SavedAsync(100);
        await GrowAsync(store);
        var newest = await SaveOfAsync(store, "ana");

        // The newer journal is stored first, so a replay in the order the store lists them would
        // let the older one win.
        await JournalAsync(store, "cy", new() { ["ana"] = newest, ["cy"] = bo }, 2101);
        await JournalAsync(store, "bo", new() { ["ana"] = older, ["bo"] = bo }, 2100);

        await StartAsync(store);
        await JoinAsync("Ana");

        Assert.Equal(900, Server.Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task ATradeJournalWithAStateThatIsNotASaveIsLeftAlone()
    {
        var (store, _, bo) = await SavedAsync(100);
        await JournalAsync(store, "bo", new() { ["ana"] = [1, 2, 3], ["bo"] = bo }, 2100);

        await StartAsync(store);
        var ana = await JoinAsync("Ana");

        Assert.True(ana.Admitted);
        Assert.Equal(100, Server.Skills.XpOf("ana", "mining"));
        Assert.Contains("is damaged and was left", Assert.Single(_logged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATradeJournalStaysWhenASaveItCoversCouldNotBeWritten()
    {
        var store = new SpyStore(new MemorySaveStore());
        await StartAsync(store);
        var (ana, bo) = await PairAsync();
        Bag("ana").Add(ItemId.Logs, 1);
        await OpenAsync(ana, bo);
        await OfferAsync(ana, ItemId.Logs, 1);
        store.Failing.Add(StoreKeys.SaveOf("bo"));

        await AcceptTwiceAsync(ana, bo);
        await _test.TickAsync(2);
        await _test.DisposeAsync();

        Assert.False((await store.ReadAsync(StoreKeys.TradeOf("ana", "bo")))!.Bytes.IsEmpty);
    }

    [Fact]
    public void ATradeJournalNamesTwoPlayersAndNobodyElse()
    {
        Assert.Throws<FormatException>(() => TradeJournal.Decode("{\"ana\":\"AA==\"}"u8));
        Assert.Throws<FormatException>(() => TradeJournal.Decode("{\"ana\":\"AA==\",\"../x\":\"AA==\"}"u8));

        var states = TradeJournal.Decode(TradeJournal.Encode(new Dictionary<string, byte[]> { ["ana"] = new byte[1], ["bo"] = new byte[2] }));

        Assert.Equal(["ana", "bo"], states.Keys);
        Assert.Equal(new byte[1], states["ana"]);
        Assert.Equal(new byte[2], states["bo"]);
    }

    [Fact]
    public void ATradeJournalHasOneKeyForAPairWhicheverWayRound()
    {
        Assert.Equal(StoreKeys.TradeOf("ana", "bo"), StoreKeys.TradeOf("bo", "ana"));
        Assert.NotEqual(StoreKeys.TradeOf("ab", "c"), StoreKeys.TradeOf("a", "bc"));
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
