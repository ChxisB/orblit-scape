using Scape.Client;
using Scape.Content;
using Scape.Core;

namespace Scape.Panels.Tests;

public sealed class PanelControllerTests
{
    [Fact]
    public async Task TheSettingsStopAtTheirEnds()
    {
        await using var game = await PanelGame.StartAsync();

        for (var i = 0; i < 30; i++)
        {
            game.Controller.StepVolume(1);
            game.Controller.StepViewDistance(-1);
        }

        Assert.Equal(PanelState.MaxVolume, game.Controller.State.Volume);
        Assert.Equal(PanelState.MinViewDistance, game.Controller.State.ViewDistance);

        for (var i = 0; i < 30; i++)
        {
            game.Controller.StepVolume(-1);
            game.Controller.StepViewDistance(1);
        }

        Assert.Equal(0, game.Controller.State.Volume);
        Assert.Equal(PanelState.MaxViewDistance, game.Controller.State.ViewDistance);
    }

    [Fact]
    public async Task AStepMovesTheSettingByItsDelta()
    {
        await using var game = await PanelGame.StartAsync();

        game.Controller.StepVolume(2);
        game.Controller.StepViewDistance(-3);

        Assert.Equal(7, game.Controller.State.Volume);
        Assert.Equal(9, game.Controller.State.ViewDistance);
    }

    [Fact]
    public async Task TabsAreKeptSeparate()
    {
        await using var game = await PanelGame.StartAsync();

        game.Controller.ShowTab(SideTab.Quests);
        game.Controller.ShowChat(ChatTab.Private);

        Assert.Equal((SideTab.Quests, ChatTab.Private), (game.Controller.State.Tab, game.Controller.State.Chat));
    }

    [Fact]
    public async Task DoesNotSayItChangedForTheSameHoverText()
    {
        await using var game = await PanelGame.StartAsync();
        var changes = 0;
        game.Controller.Changed += () => changes++;

        game.Controller.SetHover("Take Logs");
        game.Controller.SetHover("Take Logs");

        Assert.Equal(1, changes);
        Assert.Equal("Take Logs", game.Controller.State.Hover);
    }

    [Fact]
    public async Task EveryChangeIsSaidOnce()
    {
        await using var game = await PanelGame.StartAsync();
        var changes = 0;
        game.Controller.Changed += () => changes++;

        game.Controller.ShowTab(SideTab.Skills);
        game.Controller.ShowChat(ChatTab.Public);
        game.Controller.StepVolume(1);
        game.Controller.StepViewDistance(1);
        game.Controller.OpenMenu([new MenuEntry("Cancel", () => { })]);
        game.Controller.CloseMenu();
        game.Controller.Refresh();

        Assert.Equal(7, changes);
    }

    [Fact]
    public async Task AMenuEntryRunsOnceAfterTheMenuCloses()
    {
        await using var game = await PanelGame.StartAsync();
        var runs = new List<int>();
        var menuWhenRun = new List<int>();
        game.Controller.OpenMenu(
        [
            new MenuEntry("First", () => { runs.Add(0); menuWhenRun.Add(game.Controller.State.Menu.Count); }),
            new MenuEntry("Second", () => { runs.Add(1); menuWhenRun.Add(game.Controller.State.Menu.Count); }),
        ]);

        game.Controller.RunMenu(1);

        Assert.Equal([1], runs);
        Assert.Equal([0], menuWhenRun);
        Assert.Empty(game.Controller.State.Menu);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task AnIndexWithNoEntryOnlyClosesTheMenu(int index)
    {
        await using var game = await PanelGame.StartAsync();
        var ran = false;
        game.Controller.OpenMenu(
        [
            new MenuEntry("First", () => ran = true),
            new MenuEntry("Second", () => ran = true),
        ]);

        game.Controller.RunMenu(index);

        Assert.False(ran);
        Assert.Empty(game.Controller.State.Menu);
    }

    [Fact]
    public async Task AnEmptyMenuHidesTheMenu()
    {
        await using var game = await PanelGame.StartAsync();
        game.Controller.OpenMenu([new MenuEntry("Cancel", () => { })]);

        game.Controller.OpenMenu([]);

        Assert.Empty(game.Controller.State.Menu);
    }

    [Fact]
    public async Task ATapPicksUpAStackAndAnotherTapPutsItDownWhereItIs()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 1);
        await game.SettleAsync();

        game.Controller.TapSlot(ContainerId.Inventory, 0);
        Assert.Equal(new Slot(ContainerId.Inventory, 0), game.Controller.State.Picked);

        game.Controller.TapSlot(ContainerId.Inventory, 0);
        Assert.Null(game.Controller.State.Picked);
        await game.SettleAsync(2);
        Assert.Equal(new ItemStack(ItemId.Logs, 1), game.Ana.ItemAt(ContainerId.Inventory, 0));
        Assert.Empty(game.Ana.Notices);
    }

    [Fact]
    public async Task ATapOnAnEmptySlotPicksNothing()
    {
        await using var game = await PanelGame.StartAsync();

        game.Controller.TapSlot(ContainerId.Inventory, 5);

        Assert.Null(game.Controller.State.Picked);
    }

    [Fact]
    public async Task APickedStackIsPutDownWhenTheServerEmptiesItsSlot()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 1);
        await game.SettleAsync(2);
        game.Controller.TapSlot(ContainerId.Inventory, 0);
        Assert.NotNull(game.Controller.State.Picked);

        game.Bag.Take(0);
        await game.SettleAsync();

        Assert.Null(game.Controller.State.Picked);
    }

    [Fact]
    public async Task APickedStackStaysPickedWhileTheServerStillHoldsIt()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 1);
        await game.SettleAsync(2);
        game.Controller.TapSlot(ContainerId.Inventory, 0);

        await game.SettleAsync();

        Assert.Equal(new Slot(ContainerId.Inventory, 0), game.Controller.State.Picked);
    }

    [Fact]
    public async Task AWornStackIsPickedUpWhileABankIsOpen()
    {
        await using var game = await PanelGame.StartAsync();
        var worn = Wear(game);
        await game.OpenBankAsync();

        game.Controller.TapSlot(ContainerId.Equipment, worn);

        Assert.Equal(new Slot(ContainerId.Equipment, worn), game.Controller.State.Picked);
        Assert.Null(game.Ana.ItemAt(ContainerId.Bank, 0));
    }

    [Fact]
    public async Task AWornStackIsPickedUpWhileATradeIsOpen()
    {
        await using var game = await PanelGame.StartAsync();
        var worn = Wear(game);
        await game.TradeWithBoAsync();

        game.Controller.TapSlot(ContainerId.Equipment, worn);
        await game.SettleAsync(3);

        Assert.Equal(new Slot(ContainerId.Equipment, worn), game.Controller.State.Picked);
        Assert.Empty(game.Ana.PlayerTrade!.Mine);
    }

    private static int Wear(PanelGame game)
    {
        game.Game.Server.Items.ContainerOf("ana", ContainerId.Equipment)!.Add(ItemId.BronzeAxe, 1);
        return Enumerable.Range(0, ItemDef.EquipSlots.Count).Single(slot => game.Game.Server.Items.ContainerOf("ana", ContainerId.Equipment)!.At(slot) is not null);
    }
}
