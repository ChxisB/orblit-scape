using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class ContainerTests
{
    private const int MaxStack = ItemStack.MaxCount;

    private static Container Bag(ContainerId id) => Container.ForId(id, Items());

    private static Container Inventory() => Bag(ContainerId.Inventory);

    private static Container Sized(int size) => new(Items(), size);

    // Add.

    [Fact]
    public void AddPutsAStackableItemOnItsOwnStackBeforeAnEmptySlot()
    {
        var c = Inventory();

        Assert.Equal(10, c.Add(Coins, 10));
        Assert.Equal(5, c.Add(Coins, 5));
        Assert.Equal(new ItemStack(Coins, 15), c.At(0));
        Assert.Null(c.At(1));
    }

    [Fact]
    public void AddGivesANonStackableItemASlotEach()
    {
        var c = Inventory();

        Assert.Equal(3, c.Add(Logs, 3));
        int?[] counts = [.. Enumerable.Range(0, 4).Select(slot => c.At(slot)?.Count)];
        Assert.Equal([1, 1, 1, null], counts);
    }

    [Fact]
    public void AddReturnsHowManyFitWhenTheContainerIsFull()
    {
        var c = Inventory();

        Assert.Equal(Container.InventorySize, c.Add(Logs, Container.InventorySize + 5));
        Assert.Equal(0, c.Add(Logs, 1));
        Assert.Equal(0, c.Add(Coins, 1));
    }

    [Fact]
    public void AddTopsUpAStackInALaterSlotWhileAFullOneIsSkipped()
    {
        var c = Inventory();
        c.Add(Coins, MaxStack);
        c.Add(Logs, 1);
        c.Add(Coins, 7);

        Assert.Equal(new ItemStack(Coins, MaxStack), c.At(0));
        Assert.Equal(MaxStack + 7L, c.CountOf(Coins));
    }

    // Not in the Dart suite, which no mutant of the full-stack skip failed.
    [Fact]
    public void AddLeavesAFullStackItSkipsClean()
    {
        var c = Inventory();
        c.Add(Coins, MaxStack);
        c.MarkClean();
        c.Add(Coins, 7);

        Assert.Equal([1], c.Dirty);
    }

    [Fact]
    public void AddRefusesMoreOnAStackAtTheIntegerLimit()
    {
        var c = Sized(1);

        Assert.Equal(MaxStack, c.Add(Coins, MaxStack));
        Assert.Equal(0, c.Add(Coins, 1));
        Assert.Equal(new ItemStack(Coins, MaxStack), c.At(0));
    }

    [Fact]
    public void AddStopsAtTheLimitAndStartsANewStackWithTheRest()
    {
        var c = Inventory();

        Assert.Equal(MaxStack, c.Add(Coins, MaxStack));
        Assert.Equal(10, c.Add(Coins, 10));
        Assert.Equal(new ItemStack(Coins, 10), c.At(1));
    }

    [Fact]
    public void AddFillsTheContainerWithStacksWhenAskedForMoreThanFit()
    {
        Assert.Equal(MaxStack * 2L, Sized(2).Add(Coins, (MaxStack * 2L) + 5));
    }

    [Fact]
    public void AddThrowsForAnUnknownItemAndACountBelowOne()
    {
        var c = Inventory();

        Assert.ThrowsAny<ArgumentException>(() => c.Add(99, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Add(Coins, 0));
    }

    [Fact]
    public void AddPutsAWornItemOnlyInItsOwnSlot()
    {
        var c = Bag(ContainerId.Equipment);

        Assert.Equal(1, c.Add(Helmet, 2));
        Assert.Equal(new ItemStack(Helmet, 1), c.At(ItemDef.EquipSlots.ToList().IndexOf("head")));
        Assert.Equal(0, c.Add(Logs, 1));
    }

    [Fact]
    public void AddKeepsToOneTabAndSaysNoForATabThatIsNotThere()
    {
        var c = Bag(ContainerId.Bank);

        Assert.Equal(5, c.Add(Coins, 5, tab: 1));
        Assert.Equal(new ItemStack(Coins, 5), c.At(Container.BankTabSize));
        Assert.Null(c.At(0));
        Assert.Equal(0, c.Add(Coins, 5, tab: Container.BankTabs));
        Assert.Equal(0, c.Add(Coins, 5, tab: -1));
        Assert.Equal(5, c.Add(Coins, 5, tab: 0));
        Assert.Equal(new ItemStack(Coins, 5), c.At(0));
    }

    [Fact]
    public void AddToAFullTabRefusesThoughAnotherHasRoom()
    {
        var c = Bag(ContainerId.Bank);

        Assert.Equal(Container.BankTabSize, c.Add(Logs, Container.BankTabSize + 3, tab: 0));
        Assert.Equal(1, c.Add(Logs, 1, tab: 1));
    }

    // CanHold.

    [Fact]
    public void CanHoldIsTrueForAnEmptyContainerAndFalseForAFullOne()
    {
        var c = Inventory();
        Assert.True(c.CanHold(Logs));

        c.Add(Logs, Container.InventorySize);

        Assert.False(c.CanHold(Logs));
        Assert.False(c.CanHold(Coins));
    }

    [Fact]
    public void CanHoldIsTrueForAStackableItemThatHasAStackWithRoom()
    {
        var c = Inventory();
        c.Add(Coins, 5);
        c.Add(Logs, Container.InventorySize - 1);

        Assert.True(c.CanHold(Coins));
    }

    [Fact]
    public void CanHoldIsFalseForAStackAtTheLimitWhenNoSlotIsFree()
    {
        var c = Inventory();
        c.Add(Coins, MaxStack);
        c.Add(Logs, Container.InventorySize - 1);

        Assert.False(c.CanHold(Coins));
    }

    [Fact]
    public void CanHoldHonoursWhereAnItemMayGo()
    {
        var worn = Bag(ContainerId.Equipment);

        Assert.True(worn.CanHold(Helmet));
        Assert.False(worn.CanHold(Logs));
        Assert.ThrowsAny<ArgumentException>(() => worn.CanHold(99));
    }

    // Take.

    [Fact]
    public void TakeTakesPartOfAStackAllOfItAndNothingFromEmpty()
    {
        var c = Inventory();
        c.Add(Coins, 10);

        Assert.Equal(new ItemStack(Coins, 4), c.Take(0, 4));
        Assert.Equal(new ItemStack(Coins, 6), c.At(0));
        Assert.Equal(new ItemStack(Coins, 6), c.Take(0, 99));
        Assert.Null(c.At(0));
        Assert.Null(c.Take(0));
        Assert.Null(c.Take(-1));
        Assert.Null(c.Take(Container.InventorySize));
    }

    [Fact]
    public void TakeTakesTheWholeStackWithNoCountAndNothingForZero()
    {
        var c = Inventory();
        c.Add(Coins, 10);

        Assert.Null(c.Take(0, 0));
        Assert.Null(c.Take(0, -3));
        Assert.Equal(new ItemStack(Coins, 10), c.Take(0));
    }

    // Move.

    [Fact]
    public void MoveMovesAStackToAnEmptySlot()
    {
        var c = Inventory();
        c.Add(Coins, 3);

        Assert.Equal(Outcome.Done, c.Move(0, 5));
        Assert.Null(c.At(0));
        Assert.Equal(new ItemStack(Coins, 3), c.At(5));
    }

    [Fact]
    public void MoveRefusesAnEmptySourceAFullTargetAndAMissingSlot()
    {
        var c = Inventory();
        c.Add(Coins, 3);
        c.Add(Logs, 1);

        Assert.Equal(Outcome.NothingThere, c.Move(4, 5));
        Assert.Equal(Outcome.WontFit, c.Move(0, 1));
        Assert.Equal(Outcome.WontFit, c.Move(0, 0));
        Assert.Equal(Outcome.NoSuchSlot, c.Move(-1, 2));
        Assert.Equal(Outcome.NoSuchSlot, c.Move(0, Container.InventorySize));
        Assert.Equal(new ItemStack(Coins, 3), c.At(0));
    }

    [Fact]
    public void MoveRefusesASlotTheItemMayNotSitIn()
    {
        var c = Bag(ContainerId.Equipment);
        c.Add(Helmet, 1);

        Assert.Equal(Outcome.NotAllowed, c.Move(0, 1));
        Assert.Equal(new ItemStack(Helmet, 1), c.At(0));
    }

    // Swap.

    [Fact]
    public void SwapExchangesTwoStacksAndMovesOneIntoAnEmptySlot()
    {
        var c = Inventory();
        c.Add(Coins, 3);
        c.Add(Logs, 1);

        Assert.Equal(Outcome.Done, c.Swap(0, 1));
        Assert.Equal(new ItemStack(Logs, 1), c.At(0));
        Assert.Equal(new ItemStack(Coins, 3), c.At(1));
        Assert.Equal(Outcome.Done, c.Swap(1, 9));
        Assert.Equal(new ItemStack(Coins, 3), c.At(9));
        Assert.Null(c.At(1));
    }

    [Fact]
    public void SwapRefusesTwoEmptySlotsAndAMissingSlot()
    {
        var c = Inventory();

        Assert.Equal(Outcome.NothingThere, c.Swap(0, 1));
        Assert.Equal(Outcome.NoSuchSlot, c.Swap(0, Container.InventorySize));
    }

    [Fact]
    public void SwapIsNoChangeForASlotWithItself()
    {
        var c = Inventory();
        c.Add(Coins, 3);
        c.MarkClean();

        Assert.Equal(Outcome.Done, c.Swap(0, 0));
        Assert.Empty(c.Dirty);
    }

    [Fact]
    public void SwapRefusesWhenEitherItemWouldLandInASlotItMayNot()
    {
        var c = Bag(ContainerId.Equipment);
        c.Add(Helmet, 1);

        Assert.Equal(Outcome.NotAllowed, c.Swap(0, 3));
        Assert.Equal(Outcome.NotAllowed, c.Swap(3, 0));
        Assert.Equal(new ItemStack(Helmet, 1), c.At(0));
    }

    [Fact]
    public void SwapChecksTheItemInTheOtherSlotToo()
    {
        var c = Bag(ContainerId.Equipment);
        c.Add(Helmet, 1);
        c.Add(Sword, 1);

        Assert.Equal(Outcome.NotAllowed, c.Swap(0, 3));
    }

    // SwapWith.

    [Fact]
    public void SwapWithWearsAnItemAndTakesBackWhatWasWorn()
    {
        var worn = Bag(ContainerId.Equipment);
        worn.Add(Helmet, 1);
        var bagged = Inventory();
        bagged.Add(Sword, 1);

        Assert.Equal(Outcome.Done, bagged.SwapWith(worn, 0, 3));
        Assert.Equal(new ItemStack(Sword, 1), worn.At(3));
        Assert.Null(bagged.At(0));
        Assert.Equal(Outcome.Done, worn.SwapWith(bagged, 0, 5));
        Assert.Equal(new ItemStack(Helmet, 1), bagged.At(5));
    }

    [Fact]
    public void SwapWithRefusesAnItemInASlotItMayNotGoAndChangesNothing()
    {
        var worn = Bag(ContainerId.Equipment);
        var bagged = Inventory();
        bagged.Add(Coins, 3);

        Assert.Equal(Outcome.NotAllowed, bagged.SwapWith(worn, 0, 0));
        Assert.Equal(new ItemStack(Coins, 3), bagged.At(0));
        Assert.Null(worn.At(0));
    }

    [Fact]
    public void SwapWithRefusesTwoEmptySlotsAndAMissingSlot()
    {
        var worn = Bag(ContainerId.Equipment);
        var bagged = Inventory();

        Assert.Equal(Outcome.NothingThere, bagged.SwapWith(worn, 0, 0));
        Assert.Equal(Outcome.NoSuchSlot, bagged.SwapWith(worn, 0, 99));
    }

    [Fact]
    public void SwapWithIsASwapInOneContainer()
    {
        var c = Inventory();
        c.Add(Coins, 3);

        Assert.Equal(Outcome.Done, c.SwapWith(c, 0, 4));
        Assert.Equal(new ItemStack(Coins, 3), c.At(4));
    }

    // Stack.

    [Fact]
    public void StackAddsOneStackToAnotherAndEmptiesTheSource()
    {
        var c = Inventory();
        c.Add(Coins, 7);
        c.Split(0, 5, 3);

        Assert.Equal(Outcome.Done, c.Stack(0, 5));
        Assert.Null(c.At(0));
        Assert.Equal(new ItemStack(Coins, 7), c.At(5));
    }

    [Fact]
    public void StackMovesOnlyWhatFitsUnderTheLimit()
    {
        var c = Inventory();
        c.Add(Coins, MaxStack - 2);
        c.Mirror([new SlotChange(1, new ItemStack(Coins, 5))]);

        Assert.Equal(Outcome.Partly, c.Stack(1, 0));
        Assert.Equal(new ItemStack(Coins, MaxStack), c.At(0));
        Assert.Equal(new ItemStack(Coins, 3), c.At(1));
        Assert.Equal(Outcome.WontFit, c.Stack(1, 0));
    }

    [Fact]
    public void StackRefusesOtherItemsItemsThatDoNotStackItselfAndGaps()
    {
        var c = Inventory();
        c.Add(Coins, 3);
        c.Add(Logs, 2);

        Assert.Equal(Outcome.WontFit, c.Stack(0, 1));
        Assert.Equal(Outcome.WontFit, c.Stack(1, 2));
        Assert.Equal(Outcome.WontFit, c.Stack(0, 0));
        Assert.Equal(Outcome.NothingThere, c.Stack(0, 9));
        Assert.Equal(Outcome.NothingThere, c.Stack(9, 0));
        Assert.Equal(Outcome.NoSuchSlot, c.Stack(0, Container.InventorySize));
    }

    [Fact]
    public void StackRefusesTwoStacksOfANonStackableItem()
    {
        var c = Inventory();
        c.Add(Logs, 2);

        Assert.Equal(Outcome.WontFit, c.Stack(0, 1));
    }

    // Split.

    [Fact]
    public void SplitMovesPartOfAStackToAnEmptySlot()
    {
        var c = Inventory();
        c.Add(Coins, 10);

        Assert.Equal(Outcome.Done, c.Split(0, 4, 3));
        Assert.Equal(new ItemStack(Coins, 7), c.At(0));
        Assert.Equal(new ItemStack(Coins, 3), c.At(4));
    }

    [Fact]
    public void SplitRefusesACountOfNoneTheWholeStackOrMore()
    {
        var c = Inventory();
        c.Add(Coins, 10);

        Assert.Equal(Outcome.WontFit, c.Split(0, 4, 0));
        Assert.Equal(Outcome.WontFit, c.Split(0, 4, 10));
        Assert.Equal(Outcome.WontFit, c.Split(0, 4, 11));
        Assert.Equal(Outcome.Done, c.Split(0, 4, 9));
    }

    [Fact]
    public void SplitRefusesAFullTargetAnEmptySourceAndAMissingSlot()
    {
        var c = Inventory();
        c.Add(Coins, 10);
        c.Add(Logs, 1);

        Assert.Equal(Outcome.WontFit, c.Split(0, 1, 3));
        Assert.Equal(Outcome.NothingThere, c.Split(5, 6, 1));
        Assert.Equal(Outcome.NoSuchSlot, c.Split(0, -1, 1));
    }

    [Fact]
    public void SplitRefusesASlotThePieceMayNotSitIn()
    {
        var c = Bag(ContainerId.Equipment);
        c.Add(Arrows, 10);

        Assert.Equal(Outcome.NotAllowed, c.Split(3, 0, 2));
        Assert.Equal(new ItemStack(Arrows, 10), c.At(3));
    }

    // Transfer.

    [Fact]
    public void TransferDepositsAWholeStackAndWithdrawsPartOfOne()
    {
        var inv = Inventory();
        inv.Add(Coins, 10);
        var bank = Bag(ContainerId.Bank);

        Assert.Equal(Outcome.Done, inv.Transfer(0, bank));
        Assert.Null(inv.At(0));
        Assert.Equal(new ItemStack(Coins, 10), bank.At(0));
        Assert.Equal(Outcome.Done, bank.Transfer(0, inv, count: 4));
        Assert.Equal(new ItemStack(Coins, 6), bank.At(0));
        Assert.Equal(new ItemStack(Coins, 4), inv.At(0));
    }

    [Fact]
    public void TransferLeavesWhatDoesNotFitInTheSource()
    {
        var inv = Inventory();
        inv.Add(Coins, 10);
        var small = Sized(1);
        small.Add(Coins, MaxStack - 3);

        Assert.Equal(Outcome.Partly, inv.Transfer(0, small));
        Assert.Equal(new ItemStack(Coins, MaxStack), small.At(0));
        Assert.Equal(new ItemStack(Coins, 7), inv.At(0));
        Assert.Equal(Outcome.WontFit, inv.Transfer(0, small));
        Assert.Equal(new ItemStack(Coins, 7), inv.At(0));
    }

    [Fact]
    public void TransferPutsTheDepositInTheTabAskedFor()
    {
        var inv = Inventory();
        inv.Add(Coins, 10);
        var bank = Bag(ContainerId.Bank);

        Assert.Equal(Outcome.Done, inv.Transfer(0, bank, tab: 2));
        Assert.Equal(new ItemStack(Coins, 10), bank.At(2 * Container.BankTabSize));
    }

    [Fact]
    public void TransferRefusesItselfAnEmptySlotAMissingSlotAndAZeroCount()
    {
        var inv = Inventory();
        inv.Add(Coins, 10);
        var bank = Bag(ContainerId.Bank);

        Assert.Equal(Outcome.NotAllowed, inv.Transfer(0, inv));
        Assert.Equal(Outcome.NothingThere, inv.Transfer(1, bank));
        Assert.Equal(Outcome.NoSuchSlot, inv.Transfer(-1, bank));
        Assert.Equal(Outcome.WontFit, inv.Transfer(0, bank, count: 0));
        Assert.Equal(new ItemStack(Coins, 10), inv.At(0));
    }

    [Fact]
    public void TransferIsRefusedByAContainerThatWillNotTakeTheItem()
    {
        var inv = Inventory();
        inv.Add(Logs, 1);
        var worn = Bag(ContainerId.Equipment);

        Assert.Equal(Outcome.WontFit, inv.Transfer(0, worn));
        Assert.Equal(new ItemStack(Logs, 1), inv.At(0));
    }

    // Dirty.

    [Fact]
    public void DirtyListsChangedSlotsLowestFirstUntilCleaned()
    {
        var c = Inventory();
        c.Add(Logs, 1);
        c.Add(Logs, 1);
        c.Move(1, 7);

        Assert.Equal([0, 1, 7], c.Dirty);
        c.MarkClean();
        Assert.Empty(c.Dirty);
    }

    [Fact]
    public void DirtyIsUntouchedByARefusal()
    {
        var c = Inventory();
        c.Add(Logs, 1);
        c.MarkClean();

        c.Move(0, 0);
        c.Swap(5, 6);

        Assert.Empty(c.Dirty);
    }

    [Fact]
    public void DirtyIsUntouchedWhenAMirrorTakesChangesIn()
    {
        var c = Inventory();

        c.Mirror([new SlotChange(2, new ItemStack(Coins, 4))]);
        Assert.Equal(new ItemStack(Coins, 4), c.At(2));
        Assert.Empty(c.Dirty);

        c.Mirror([new SlotChange(99, new ItemStack(Coins, 1))]);
        c.Mirror([new SlotChange(2, null)]);
        Assert.Null(c.At(2));
    }

    // Container.

    [Fact]
    public void ContainerRejectsASizeOrTabSizeThatDoesNotDivide()
    {
        var items = Items();

        Assert.ThrowsAny<ArgumentException>(() => new Container(items, 0));
        Assert.ThrowsAny<ArgumentException>(() => new Container(items, 10, tabSize: 3));
        Assert.ThrowsAny<ArgumentException>(() => new Container(items, 10, tabSize: 0));
    }

    [Fact]
    public void ContainerHasTheSizesTheGamePromises()
    {
        Assert.Equal(28, Inventory().Size);
        Assert.Equal(ItemDef.EquipSlots.Count, Bag(ContainerId.Equipment).Size);
        Assert.Equal(Container.BankTabs * Container.BankTabSize, Bag(ContainerId.Bank).Size);
    }

    [Fact]
    public void ContainerIsNotMadeForAnIdOutsideTheEnum()
    {
        Assert.Throws<InvalidOperationException>(() => Container.SizeOf((ContainerId)3));
        Assert.Throws<InvalidOperationException>(() => Container.ForId((ContainerId)3, Items()));
    }

    // Random operations.

    [Fact]
    public void RandomOperationsNeverChangeTheTotalOfAnyItem()
    {
        var random = new Random(7);
        var items = Items();
        Container[] all = [Inventory(), Bag(ContainerId.Bank), Bag(ContainerId.Equipment)];
        var pool = new Dictionary<int, long> { [Coins] = MaxStack, [Logs] = 200, [Sword] = 3, [Helmet] = 3, [Arrows] = 500 };
        var total = new Dictionary<int, long>(pool);
        long Counted(int item) => pool[item] + all.Sum(c => c.CountOf(item));

        for (var i = 0; i < 20000; i++)
        {
            Step(random, items, all, pool);
            if (i % 500 == 0)
            {
                Assert.All(total, entry => Assert.Equal(entry.Value, Counted(entry.Key)));
            }
        }

        Assert.All(total, entry => Assert.Equal(entry.Value, Counted(entry.Key)));
        Assert.All(all.SelectMany(c => c.Slots).OfType<ItemStack>(), stack => Assert.True(items.Require(stack.Item).Stackable || stack.Count == 1));
    }

    private static void Step(Random random, ItemSet items, Container[] all, Dictionary<int, long> pool)
    {
        var from = all[random.Next(all.Length)];
        var to = all[random.Next(all.Length)];
        var a = random.Next(from.Size);
        var b = random.Next(to.Size);
        var item = items.Items[random.Next(items.Items.Count)].Id;
        var count = DrawCount(random);
        switch (random.Next(8))
        {
            case 0:
                Put(from, item, count, pool);
                break;
            case 1:
                Return(from.Take(a, count), pool);
                break;
            case 2:
                from.Move(a, random.Next(from.Size));
                break;
            case 3:
                from.Swap(a, random.Next(from.Size));
                break;
            case 4:
                from.Stack(a, random.Next(from.Size));
                break;
            case 5:
                from.Split(a, random.Next(from.Size), count);
                break;
            case 6:
                from.Transfer(a, to, count, random.Next(2) == 1 ? b : null);
                break;
            default:
                from.Transfer(a, to);
                break;
        }
    }

    /// <summary>A small count half the time, so stacks also split and merge, not only fill.</summary>
    private static int DrawCount(Random random) => 1 + random.Next(random.Next(2) == 1 ? 5 : MaxStack);

    private static void Put(Container into, int item, int count, Dictionary<int, long> pool)
    {
        var wanted = Math.Min(count, pool[item]);
        pool[item] -= wanted == 0 ? 0 : into.Add(item, wanted);
    }

    private static void Return(ItemStack? taken, Dictionary<int, long> pool)
    {
        if (taken is { } stack)
        {
            pool[stack.Item] += stack.Count;
        }
    }

    // TryExchange.

    [Fact]
    public void TryExchangeAnswersAsExchangeWouldAndChangesNothing()
    {
        var c = Sized(2);
        c.Add(Coins, 10);
        c.Add(Logs, 1);
        c.MarkClean();

        Assert.Equal(Outcome.Done, c.TryExchange(take: [new ItemStack(Logs, 1)], give: [new ItemStack(Logs, 1)]));
        Assert.Equal(Outcome.WontFit, c.TryExchange(give: [new ItemStack(Logs, 1)]));
        Assert.Equal(Outcome.NothingThere, c.TryExchange(take: [new ItemStack(Coins, 11)]));
        Assert.Equal(1, c.CountOf(Logs));
        Assert.Equal(10, c.CountOf(Coins));
        Assert.Empty(c.Dirty);
    }

    // Exchange.

    [Fact]
    public void ExchangeTakesAndGivesAsOneChange()
    {
        var c = Inventory();
        c.Add(Coins, 50);

        var outcome = c.Exchange(take: [new ItemStack(Coins, 30)], give: [new ItemStack(Logs, 2)]);

        Assert.Equal(Outcome.Done, outcome);
        Assert.Equal(20, c.CountOf(Coins));
        Assert.Equal(2, c.CountOf(Logs));
    }

    [Fact]
    public void ExchangeTakesAcrossStacks()
    {
        var c = Sized(4);
        c.Add(Logs, 3);

        c.Exchange(take: [new ItemStack(Logs, 2)]);

        Assert.Equal(1, c.CountOf(Logs));
    }

    [Fact]
    public void ExchangeChangesNothingWhenTheTakeIsNotAllHeld()
    {
        var c = Inventory();
        c.Add(Coins, 5);
        c.MarkClean();

        var outcome = c.Exchange(take: [new ItemStack(Coins, 6)], give: [new ItemStack(Logs, 1)]);

        Assert.Equal(Outcome.NothingThere, outcome);
        Assert.Equal(5, c.CountOf(Coins));
        Assert.Equal(0, c.CountOf(Logs));
        Assert.Empty(c.Dirty);
    }

    [Fact]
    public void ExchangeChangesNothingAndMarksNothingWhenTheGiveDoesNotFit()
    {
        var c = Sized(2);
        c.Add(Coins, 10);
        c.Add(Logs, 1);
        c.MarkClean();

        var outcome = c.Exchange(take: [new ItemStack(Coins, 4)], give: [new ItemStack(Logs, 2)]);

        Assert.Equal(Outcome.WontFit, outcome);
        Assert.Equal(new ItemStack(Coins, 10), c.At(0));
        Assert.Equal(new ItemStack(Logs, 1), c.At(1));
        Assert.Empty(c.Dirty);
    }

    [Fact]
    public void ExchangeKeepsTheSlotsDirtyFromBeforeARefusedExchange()
    {
        var c = Sized(1);
        c.Add(Coins, 10);
        Assert.Equal([0], c.Dirty);

        c.Exchange(take: [new ItemStack(Coins, 1)], give: [new ItemStack(Logs, 2)]);

        Assert.Equal([0], c.Dirty);
    }

    [Fact]
    public void ExchangeLetsTheGiveUseASlotTheTakeFreed()
    {
        var c = Sized(1);
        c.Add(Coins, 10);

        var outcome = c.Exchange(take: [new ItemStack(Coins, 10)], give: [new ItemStack(Logs, 1)]);

        Assert.Equal(Outcome.Done, outcome);
        Assert.Equal(new ItemStack(Logs, 1), c.At(0));
    }

    [Fact]
    public void ExchangeThrowsForAnItemThatDoesNotExistBeforeChangingAnything()
    {
        var c = Inventory();
        c.Add(Coins, 5);
        c.MarkClean();

        Assert.ThrowsAny<ArgumentException>(() => c.Exchange(take: [new ItemStack(Coins, 1)], give: [new ItemStack(99, 1)]));
        Assert.Equal(5, c.CountOf(Coins));
        Assert.Empty(c.Dirty);
    }

    // A view.

    private static Container ViewBank()
    {
        var c = Bag(ContainerId.Bank);
        c.Add(Logs, 2, tab: 0);
        c.Add(Coins, 9, tab: 3);
        c.Add(Sword, 1, tab: 3);
        return c;
    }

    [Fact]
    public void ViewListsTheOccupiedSlotsOfATab()
    {
        var c = ViewBank();

        Assert.Equal([300, 301], c.Occupied(3));
        Assert.Equal([0, 1], c.Occupied(0));
        Assert.Empty(c.Occupied(1));
    }

    [Fact]
    public void ViewListsEveryOccupiedSlotWithNoTab()
    {
        Assert.Equal([0, 1, 300, 301], ViewBank().Occupied());
    }

    [Fact]
    public void ViewOfATabThatDoesNotExistIsEmpty()
    {
        Assert.Empty(ViewBank().Occupied(Container.BankTabs));
        Assert.Empty(ViewBank().Occupied(-1));
    }

    [Fact]
    public void ViewSearchesNamesWhateverTheCase()
    {
        var c = ViewBank();

        Assert.Equal([0, 1], c.Search("LOG"));
        Assert.Equal([300], c.Search("oin"));
        Assert.Empty(c.Search("zzz"));
    }

    [Fact]
    public void ViewSearchesInsideOneTab()
    {
        var c = ViewBank();

        Assert.Equal([300, 301], c.Search("o", tab: 3));
        Assert.Empty(c.Search("log", tab: 3));
    }

    [Fact]
    public void ViewOfAnEmptySearchFindsEverythingHeld()
    {
        Assert.Equal([0, 1, 300, 301], ViewBank().Search(string.Empty));
    }

    // Restore.

    [Fact]
    public void RestorePutsSavedStacksBackAndMarksEachSlotChanged()
    {
        var saved = Inventory();
        saved.Add(Coins, 40);
        saved.Add(Logs, 2);
        var c = Inventory();

        Assert.Equal(0, c.Restore(saved.Slots));
        Assert.Equal(saved.Slots, c.Slots);
        Assert.Equal([0, 1, 2], c.Dirty);
    }

    [Fact]
    public void RestoreLeavesOutAnItemTheGameNoLongerHas()
    {
        var c = Inventory();

        var lost = c.Restore([new ItemStack(99, 1), new ItemStack(Coins, 5)]);

        Assert.Equal(1, lost);
        Assert.Null(c.At(0));
        Assert.Equal(new ItemStack(Coins, 5), c.At(1));
    }

    [Fact]
    public void RestoreLeavesOutAStackOfANonStackableItemThatHoldsMany()
    {
        var c = Inventory();

        var lost = c.Restore([new ItemStack(Logs, 3), new ItemStack(Logs, 1)]);

        Assert.Equal(1, lost);
        Assert.Null(c.At(0));
        Assert.Equal(new ItemStack(Logs, 1), c.At(1));
    }

    [Fact]
    public void RestoreLeavesOutAStackItsSlotNoLongerAccepts()
    {
        var c = Bag(ContainerId.Equipment);

        var lost = c.Restore([new ItemStack(Sword, 1), null, null, new ItemStack(Sword, 1)]);

        Assert.Equal(1, lost);
        Assert.Null(c.At(0));
        Assert.Equal(new ItemStack(Sword, 1), c.At(3));
    }

    [Fact]
    public void RestoreLeavesOutAStackPastTheLastSlot()
    {
        var c = Inventory();
        var saved = new ItemStack?[Container.InventorySize + 1];
        saved[Container.InventorySize] = new ItemStack(Coins, 1);

        Assert.Equal(1, c.Restore(saved));
        Assert.Empty(c.Occupied());
    }

    [Fact]
    public void RestoreRefusesAContainerThatAlreadyHoldsSomething()
    {
        var c = Inventory();
        c.Add(Coins, 1);

        Assert.Throws<InvalidOperationException>(() => c.Restore([new ItemStack(Coins, 1)]));
    }

    [Fact]
    public void RestoreSlotsIsACopyThatCannotBeChanged()
    {
        var c = Inventory();
        c.Add(Coins, 1);
        var before = c.Slots;

        c.Add(Logs, 1);

        Assert.Null(before[1]);
        Assert.Throws<NotSupportedException>(() => { ((IList<ItemStack?>)before)[0] = null; });
    }
}
