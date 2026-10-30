using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Orblit.Quest;
using Scape.Client;
using Scape.Client.Tests;
using Scape.Content;
using Scape.Core;

namespace Scape.Panels.Tests;

public sealed class ShellTests
{
    [Fact]
    public async Task TheInventoryShowsWhatTheServerHolds()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Coins, 5);
        game.Bag.Add(ItemId.Logs, 1);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();

            Assert.Contains("Coins x5", PanelGame.Labels(window));
            Assert.Contains("Logs", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task ADragMovesTheStackOnceTheServerConfirms()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Coins, 5);
        game.Bag.Add(ItemId.Logs, 1);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();
            double Left(string text) => PanelGame.TopLeft(window, PanelGame.ButtonsSaying(window, text)[0]).X;
            Assert.True(Left("Coins x5") < Left("Logs"));

            PanelGame.Tap(window, "Coins x5");
            PanelGame.Tap(window, "Logs");
            Assert.True(Left("Coins x5") < Left("Logs"));
            await game.SettleAsync();

            Assert.True(Left("Logs") < Left("Coins x5"));
        });
    }

    [Fact]
    public async Task ARefusedDragLeavesThePanelAsItWas()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Coins, 5);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();

            PanelGame.Tap(window, "Coins x5");
            PanelGame.Tap(window, "equ");
            PanelGame.TapCell(window, ContainerId.Equipment, 0);
            await game.SettleAsync(2);
            PanelGame.Tap(window, "inv");

            Assert.Equal(new ItemStack(ItemId.Coins, 5), game.Ana.ItemAt(ContainerId.Inventory, 0));
            Assert.Equal("That does not go there.", game.Ana.Notices[^1]);
            Assert.Contains("Coins x5", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task ThePickedCellIsAmber()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Coins, 5);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();
            Assert.NotEqual(Amber, ColourOf(PanelGame.CellOf(window, ContainerId.Inventory, 0)));

            PanelGame.TapCell(window, ContainerId.Inventory, 0);

            Assert.Equal(Amber, ColourOf(PanelGame.CellOf(window, ContainerId.Inventory, 0)));
            Assert.NotEqual(Amber, ColourOf(PanelGame.CellOf(window, ContainerId.Inventory, 1)));
        });
    }

    [Fact]
    public async Task EveryInventorySlotHasACell()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();

            var slots = Enumerable.Range(0, Container.InventorySize).Select(slot => PanelGame.CellOf(window, ContainerId.Inventory, slot));
            Assert.All(slots, cell => Assert.Equal(40, cell.Bounds.Height));
        });
    }

    [Fact]
    public async Task SkillsListsEachSkillWithItsLevel()
    {
        await using var game = await PanelGame.StartAsync();
        game.Game.Server.Skills.Grant("ana", "mining", DefaultSkills.All.Curve.XpForLevel(5));
        await game.SettleAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "ski");

            foreach (var skill in DefaultSkills.All.Skills)
            {
                var name = Headless.Shown<TextBlock>(window).Single(text => text.Text == skill.Name);
                var level = Assert.IsType<Grid>(name.Parent).Children.OfType<TextBlock>().Last().Text;
                Assert.Equal(game.Ana.LevelOf(skill.Id).ToString(CultureInfo.InvariantCulture), level);
            }

            var mining = Headless.Shown<TextBlock>(window).Single(text => text.Text == "Mining");
            Assert.Equal("5", Assert.IsType<Grid>(mining.Parent).Children.OfType<TextBlock>().Last().Text);

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task QuestsSaysSoWhenNoneHasBegun()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "que");

            Assert.Contains("No quests begun.", PanelGame.Labels(window));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EquipmentNamesEachSlot()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "equ");

            var labels = PanelGame.Labels(window);
            Assert.All(ItemDef.EquipSlots, slot => Assert.Contains(slot, labels));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FriendsSaysSoWhenThereAreNone()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "fri");

            Assert.Contains("No friends added.", PanelGame.Labels(window));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FriendsListsWhoIsOnlineAndWhoIsIgnored()
    {
        await using var game = await PanelGame.StartAsync();
        await game.Game.JoinAsync("Bo");
        game.Ana.EditList(ListKind.Friends, "bo", add: true);
        game.Ana.EditList(ListKind.Ignored, "cy", add: true);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(3);
            PanelGame.Tap(window, "fri");

            var labels = PanelGame.Labels(window);
            Assert.Contains("bo (online)", labels);
            Assert.Contains("cy (ignored)", labels);
            Assert.DoesNotContain("No friends added.", labels);
        });
    }

    [Fact]
    public async Task TheSettingsTabStepsBothSettingsAndShowsThem()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "set");
            Assert.Contains("5", PanelGame.Labels(window));
            Assert.Contains("12", PanelGame.Labels(window));

            Headless.Click(window, PanelGame.ButtonsSaying(window, "+")[0]);
            Headless.Click(window, PanelGame.ButtonsSaying(window, "-")[1]);

            Assert.Equal((6, 11), (game.Controller.State.Volume, game.Controller.State.ViewDistance));
            Assert.Contains("6", PanelGame.Labels(window));
            Assert.Contains("11", PanelGame.Labels(window));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AConversationShowsInTheSidePanelAndAChoiceBeginsAQuest()
    {
        await using var game = await PanelGame.StartAsync();
        var brindle = (long)game.Game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Lamplighter, PanelGame.Beside)).NetworkId;

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);

            game.Ana.Talk(brindle);
            await game.SettleAsync(6);
            Assert.Contains("Brindle", PanelGame.Labels(window));
            PanelGame.Tap(window, "Continue");
            await game.SettleAsync(3);
            PanelGame.Tap(window, "I can help.");
            await game.SettleAsync(4);
            PanelGame.Tap(window, "Continue");
            await game.SettleAsync(2);
            PanelGame.Tap(window, "que");

            Assert.Contains("The Dark Lantern", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task AFinishedQuestIsMarkedDone()
    {
        await using var game = await PanelGame.StartAsync();
        var quests = game.Game.Server.Quests!;
        var table = quests.TableOf("ana")!;
        var last = DefaultStory.Story.Book.Quest("lantern").Last;
        Assert.True(quests.Commit("ana", table, table.Set(QuestNames.StageName("lantern"), last).Table));
        await game.SettleAsync();

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "que");

            Assert.Contains("The Dark Lantern (done)", PanelGame.Labels(window));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ASecondChoiceIsTheOneSent()
    {
        await using var game = await PanelGame.StartAsync();
        var brindle = (long)game.Game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Lamplighter, PanelGame.Beside)).NetworkId;

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);
            game.Ana.Talk(brindle);
            await game.SettleAsync(6);
            PanelGame.Tap(window, "Continue");
            await game.SettleAsync(3);

            PanelGame.Tap(window, "Not now.");
            await game.SettleAsync(3);
            PanelGame.Tap(window, "que");

            Assert.Contains("No quests begun.", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task AShopLineBuysOne()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Coins, 100);
        var keeper = (long)game.Game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, PanelGame.Beside)).NetworkId;

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);
            game.Ana.Trade(keeper);
            await game.SettleAsync(6);

            PanelGame.Tap(window, "Bronze axe (5 left)");
            await game.SettleAsync(3);

            Assert.Equal(1, game.Bag.CountOf(ItemId.BronzeAxe));
            Assert.Contains("Bronze axe (4 left)", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task AnOpenBankTakesATapOnTheInventoryAndGivesItBack()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Feathers, 8);

        await game.ShowAsync(async window =>
        {
            await game.OpenBankAsync();
            Assert.Contains("Bank", PanelGame.Labels(window));

            PanelGame.Tap(window, "Feathers x8");
            await game.SettleAsync(3);
            Assert.Equal(new ItemStack(ItemId.Feathers, 1), game.Ana.ItemAt(ContainerId.Bank, 0));
            game.Controller.Refresh();
            Headless.Render();
            Assert.Single(PanelGame.ButtonsSaying(window, "Feathers"));

            PanelGame.Tap(window, "Feathers");
            await game.SettleAsync(3);
            Assert.Null(game.Ana.ItemAt(ContainerId.Bank, 0));
            Assert.Equal(8, game.Bag.CountOf(ItemId.Feathers));
        });
    }

    [Fact]
    public async Task AnOpenTradeOffersOneMoreOfATappedStack()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 3);
        game.Bag.Add(ItemId.Feathers, 4);
        var bo = await game.TradeWithBoAsync();
        game.Game.Server.Items.ContainerOf("bo", ContainerId.Inventory)!.Add(ItemId.Feathers, 3);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();
            Assert.Contains("Trading with bo", PanelGame.Labels(window));

            PanelGame.Tap(window, "Logs");
            await game.SettleAsync(3);
            PanelGame.Tap(window, "Logs");
            await game.SettleAsync(3);

            Assert.Equal([new ItemStack(ItemId.Logs, 2)], game.Ana.PlayerTrade!.Mine);
            Assert.Contains("You offer: Logs x2", PanelGame.Labels(window));
            Assert.Contains("They offer: nothing", PanelGame.Labels(window));

            PanelGame.Tap(window, "Feathers x4");
            await game.SettleAsync(3);
            Assert.Contains(new ItemStack(ItemId.Feathers, 1), game.Ana.PlayerTrade!.Mine);

            bo.Offer(ItemId.Feathers, 3);
            await game.SettleAsync(3);
            Assert.Contains("They offer: Feathers x3", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task ATradeAcceptsThenConfirms()
    {
        await using var game = await PanelGame.StartAsync();
        var bo = await game.TradeWithBoAsync();

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();
            Assert.Empty(PanelGame.ButtonsSaying(window, "Confirm"));

            PanelGame.Tap(window, "Accept");
            await game.SettleAsync(3);
            bo.AcceptTrade();
            await game.SettleAsync(3);

            Assert.Single(PanelGame.ButtonsSaying(window, "Confirm"));
            Assert.Empty(PanelGame.ButtonsSaying(window, "Accept"));
            PanelGame.Tap(window, "Decline");
            await game.SettleAsync(3);
            Assert.Null(game.Ana.PlayerTrade);
            Assert.DoesNotContain("Decline", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task ALongBankScrollsAndKeepsTheInventoryInReachOnAPhone()
    {
        await using var game = await PanelGame.StartAsync();
        FillBank(game);

        await game.ShowAsync(
            async window =>
            {
                await game.OpenBankAsync();

                Assert.Single(Headless.Shown<ScrollViewer>(window), scroll => scroll.Height == 120);
                var last = PanelGame.CellOf(window, ContainerId.Inventory, Container.InventorySize - 1);
                Assert.True(PanelGame.TopLeft(window, last).Y + last.Bounds.Height <= PanelGame.Height);
            },
            width: 360);
    }

    [Fact]
    public async Task TheBankKeepsItsScrollOffsetAcrossARedraw()
    {
        await using var game = await PanelGame.StartAsync();
        FillBank(game);

        await game.ShowAsync(async window =>
        {
            await game.OpenBankAsync();
            var scroll = Headless.Shown<ScrollViewer>(window).Single(one => one.Height == 120);
            scroll.Offset = new Vector(0, 60);
            Headless.Render();
            Assert.Equal(60, scroll.Offset.Y);

            await game.SettleAsync();
            game.Controller.Refresh();
            Headless.Render();

            Assert.Equal(60, Headless.Shown<ScrollViewer>(window).Single(one => one.Height == 120).Offset.Y);
        });
    }

    [Fact]
    public async Task AWithdrawalFromALongBankTakesTheLineTapped()
    {
        await using var game = await PanelGame.StartAsync();
        var vault = game.Game.Server.Items.ContainerOf("ana", ContainerId.Bank)!;
        vault.Add(ItemId.Logs, 1, tab: 0);
        vault.Add(ItemId.Feathers, 9, tab: 0);

        await game.ShowAsync(async window =>
        {
            await game.OpenBankAsync();

            PanelGame.Tap(window, "Feathers x9");
            await game.SettleAsync(3);

            Assert.Equal(1, game.Bag.CountOf(ItemId.Feathers));
            Assert.Equal(0, game.Bag.CountOf(ItemId.Logs));
        });
    }

    [Fact]
    public async Task APickedStackIsPutDownWhenTheServerEmptiesItsSlot()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 1);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);
            PanelGame.Tap(window, "Logs");
            Assert.NotNull(game.Controller.State.Picked);

            game.Bag.Take(0);
            await game.SettleAsync();

            Assert.Null(game.Controller.State.Picked);
            Assert.NotEqual(Amber, ColourOf(PanelGame.CellOf(window, ContainerId.Inventory, 0)));
        });
    }

    [Fact]
    public async Task TheSidePanelStandsAtTheRightWhenTheWindowIsWide()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            var tabs = PanelGame.ButtonsSaying(window, "inv")[0].Parent as Control;
            var map = Map(window);

            Assert.Equal(320 - 16, tabs!.Bounds.Width, 1);
            Assert.Equal(1280 - 320 + 8, PanelGame.TopLeft(window, tabs).X, 1);
            Assert.True(PanelGame.TopLeft(window, map).X + map.Bounds.Width < PanelGame.TopLeft(window, tabs).X);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheSidePanelSitsUnderTheWorldWhenTheWindowIsNarrow()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(
            window =>
            {
                var tabs = PanelGame.ButtonsSaying(window, "inv")[0].Parent as Control;
                var map = Map(window);

                Assert.Equal(360 - 16, tabs!.Bounds.Width, 1);
                Assert.Equal(8, PanelGame.TopLeft(window, tabs).X, 1);
                Assert.True(PanelGame.TopLeft(window, map).Y + map.Bounds.Height < PanelGame.TopLeft(window, tabs).Y);
                Assert.True(PanelGame.TopLeft(window, tabs).Y > PanelGame.Height / 2 - 100);
                return Task.CompletedTask;
            },
            width: 360);
    }

    [Fact]
    public async Task TheSideChangesPlaceWhenTheWindowIsResized()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            var tabs = PanelGame.ButtonsSaying(window, "inv")[0].Parent as Control;
            var wideAt = PanelGame.TopLeft(window, tabs!);

            window.Width = 360;
            Headless.Render();
            var narrowAt = PanelGame.TopLeft(window, tabs!);

            Assert.True(narrowAt.X < wideAt.X);
            Assert.True(narrowAt.Y > wideAt.Y);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheInventoryIsFourCellsWide()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            Point At(int slot) => PanelGame.TopLeft(window, PanelGame.CellOf(window, ContainerId.Inventory, slot));

            Assert.Equal(At(0).Y, At(3).Y);
            Assert.True(At(3).X > At(0).X);
            Assert.Equal(At(0).X, At(4).X);
            Assert.True(At(4).Y > At(0).Y);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheOpenTabsAreLighterThanTheRest()
    {
        await using var game = await PanelGame.StartAsync();
        var open = Color.Parse("#64748b");
        var shut = Color.Parse("#334155");

        await game.ShowAsync(window =>
        {
            Color Of(string text) => ColourOf(PanelGame.ButtonsSaying(window, text)[0]);

            Assert.Equal([open, shut, shut], [Of("inv"), Of("ski"), Of("public")]);
            Assert.Equal(open, Of("game"));

            PanelGame.Tap(window, "ski");
            PanelGame.Tap(window, "public");

            Assert.Equal([shut, open, open, shut], [Of("inv"), Of("ski"), Of("public"), Of("game")]);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ACreatureAtTheEdgeOfTheMapHasItsDotKeptInside()
    {
        await using var game = await PanelGame.StartAsync();
        game.Game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Guard, new TileCoord(48, 16, 0)));

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(3);

            var guard = Assert.Single(Map(window).Children.OfType<Border>(), dot => ColourOf(dot) == Color.Parse("#facc15"));
            Assert.Equal((96.0, 96.0), (Canvas.GetLeft(guard), Canvas.GetTop(guard)));
        });
    }

    [Theory]
    [InlineData(768, true)]
    [InlineData(767, false)]
    public async Task TheSidePanelMovesToTheRightFrom768(double width, bool atRight)
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(
            window =>
            {
                var tabs = PanelGame.ButtonsSaying(window, "inv")[0].Parent as Control;

                Assert.Equal(atRight, PanelGame.TopLeft(window, tabs!).X > 8);
                return Task.CompletedTask;
            },
            width: width);
    }

    [Fact]
    public async Task AShellMeasuredWithoutALimitAsksForNothing()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            var shell = Assert.IsType<ScapeShell>(window.Content);

            shell.Measure(Size.Infinity);

            Assert.Equal(default, shell.DesiredSize);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AShellArrangedSmallerThanItWasMeasuredKeepsTheSidePanelInsideIt()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            var shell = Assert.IsType<ScapeShell>(window.Content);
            shell.Measure(new Size(360, PanelGame.Height));
            shell.Arrange(new Rect(0, 0, 360, 200));

            var side = PanelGame.ButtonsSaying(window, "inv")[0].FindAncestorOfType<Border>()!;
            Assert.Equal((0.0, 200.0), (side.Bounds.Y, side.Bounds.Height));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ANarrowShellMeasuresTheSidePanelAtItsOwnWidth()
    {
        await using var game = await PanelGame.StartAsync();
        game.Ana.Say("hi");
        game.Ana.Say(string.Join(' ', Enumerable.Repeat("word", 10)));

        await game.ShowAsync(
            async window =>
            {
                await game.SettleAsync(3);
                PanelGame.Tap(window, "public");

                var lines = Headless.Shown<TextBlock>(window).Where(text => text.Text?.StartsWith("You: ", StringComparison.Ordinal) == true).ToList();
                Assert.Equal(2, lines.Count);
                Assert.Equal(lines[0].Bounds.Height, lines[1].Bounds.Height);
            },
            width: 700);
    }

    [Fact]
    public async Task TheSidePanelFillsTheHeightOfTheWindowAtTheRight()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(window =>
        {
            var side = PanelGame.ButtonsSaying(window, "inv")[0].FindAncestorOfType<Border>()!;

            Assert.Equal((0.0, PanelGame.Height), (PanelGame.TopLeft(window, side).Y, side.Bounds.Height));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ALongChatLineWrapsInsideTheSidePanel()
    {
        await using var game = await PanelGame.StartAsync();
        game.Ana.Say(string.Join(' ', Enumerable.Repeat("word", 30)));

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(3);
            PanelGame.Tap(window, "public");

            var line = Headless.Shown<TextBlock>(window).Single(text => text.Text?.StartsWith("You: word", StringComparison.Ordinal) == true);
            Assert.True(line.Bounds.Height > 40);
            Assert.True(line.Bounds.Width <= 320);
        });
    }

    [Fact]
    public async Task TheHoverTextAndTheMenuShowInTheWorld()
    {
        await using var game = await PanelGame.StartAsync();
        game.Game.Server.Ground.Drop(ItemId.Logs, 1, PanelGame.Beside);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);
            game.Controller.SetHover("Talk to Guard");
            Headless.Render();
            Assert.Contains("Talk to Guard", PanelGame.Labels(window));
            Assert.DoesNotContain("Cancel", PanelGame.Labels(window));

            game.Controller.OpenMenu(MenusOf(game).OnGround(game.Ana.GroundItems[0]));
            Headless.Render();

            Assert.Contains("Take Logs", PanelGame.Labels(window));
            Assert.Contains("Cancel", PanelGame.Labels(window));
            PanelGame.Tap(window, "Cancel");
            Assert.DoesNotContain("Cancel", PanelGame.Labels(window));
            Assert.Empty(game.Ana.Notices);
        });
    }

    [Fact]
    public async Task AMenuEntryRunsOnceAndTheMenuGoes()
    {
        await using var game = await PanelGame.StartAsync();
        game.Game.Server.Ground.Drop(ItemId.Logs, 1, PanelGame.Beside);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(2);

            game.Controller.OpenMenu(MenusOf(game).OnGround(game.Ana.GroundItems.Single()));
            Headless.Render();
            PanelGame.Tap(window, "Take Logs");
            await game.SettleAsync(3);

            Assert.Empty(game.Controller.State.Menu);
            Assert.Equal(new ItemStack(ItemId.Logs, 1), game.Ana.ItemAt(ContainerId.Inventory, 0));
            Assert.DoesNotContain("Cancel", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task ATapOnTheSecondMenuEntryRunsThatEntryAlone()
    {
        await using var game = await PanelGame.StartAsync();
        var ran = new List<string>();
        game.Controller.OpenMenu([new MenuEntry("First", () => ran.Add("first")), new MenuEntry("Second", () => ran.Add("second"))]);

        await game.ShowAsync(window =>
        {
            PanelGame.Tap(window, "Second");

            Assert.Equal(["second"], ran);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheMinimapDrawsTheCentreAndACreatureWhereItStands()
    {
        await using var game = await PanelGame.StartAsync();
        game.Game.Server.Npcs.Spawn(new NpcSpawn(NpcId.Guard, new TileCoord(36, 34, 0)));

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(3);

            var dots = Map(window).Children.OfType<Border>().ToList();
            Assert.Equal(2, dots.Count);
            var self = Assert.Single(dots, dot => ColourOf(dot) == Color.Parse("#ffffff"));
            var guard = Assert.Single(dots, dot => ColourOf(dot) == Color.Parse("#facc15"));
            Assert.Equal((48.0, 48.0), (Canvas.GetLeft(self), Canvas.GetTop(self)));
            Assert.Equal(60.5, Canvas.GetLeft(guard), 1);
            Assert.Equal(41.75, Canvas.GetTop(guard), 1);
        });
    }

    [Fact]
    public async Task AnotherPlayerOnTheMinimapIsBlue()
    {
        await using var game = await PanelGame.StartAsync();
        var bo = await game.Game.JoinAsync("Bo");
        bo.WalkTo(PanelGame.Beside);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync(4);

            Assert.Single(Map(window).Children.OfType<Border>(), dot => ColourOf(dot) == Color.Parse("#38bdf8"));
        });
    }

    [Fact]
    public async Task TheChatShowsTheLastSixLinesOfTheTabChosen()
    {
        await using var game = await PanelGame.StartAsync();

        await game.ShowAsync(async window =>
        {
            for (var line = 1; line <= 8; line++)
            {
                game.Ana.Say($"line {line}");
            }

            await game.SettleAsync(3);
            PanelGame.Tap(window, "public");

            var labels = PanelGame.Labels(window);
            Assert.DoesNotContain("You: line 2", labels);
            Assert.Contains("You: line 3", labels);
            Assert.Contains("You: line 8", labels);
            Assert.Equal(ChatTab.Public, game.Controller.State.Chat);
            PanelGame.Tap(window, "private");
            Assert.DoesNotContain("You: line 8", PanelGame.Labels(window));
        });
    }

    [Fact]
    public async Task AnItemTheShellDoesNotKnowReadsAsItsNumber()
    {
        await using var game = await PanelGame.StartAsync();
        game.Bag.Add(ItemId.Logs, 1);
        var older = new ItemSet([.. DefaultItems.All.Items.Where(item => item.Id != ItemId.Logs)], DefaultSkills.All);

        await game.ShowAsync(
            async window =>
            {
                await game.SettleAsync();

                Assert.Contains($"Item {ItemId.Logs}", PanelGame.Labels(window));
            },
            items: older);
    }

    [Fact]
    public async Task AShellThatIsOffTheScreenDoesNotDraw()
    {
        await using var game = await PanelGame.StartAsync();

        await Headless.Run(() =>
        {
            var shell = new ScapeShell(game.Controller, DefaultItems.All, DefaultSkills.All);
            var window = new Window { Content = shell };
            window.Show();
            var drawn = shell.Content;
            window.Content = null;
            window.Close();

            game.Controller.ShowTab(SideTab.Skills);
            game.Controller.Refresh();

            Assert.NotNull(drawn);
            Assert.Same(drawn, shell.Content);
        });
    }

    [Fact]
    public async Task AButtonActsWhenPressedEvenIfARedrawReplacesItBeforeItIsLetGo()
    {
        await using var game = await PanelGame.StartAsync();
        var acted = 0;
        game.Controller.OpenMenu([new MenuEntry("Wave", () => acted++)]);

        await game.ShowAsync(async window =>
        {
            await game.SettleAsync();
            var wave = PanelGame.ButtonsSaying(window, "Wave")[0];
            var at = wave.TranslatePoint(new Point(wave.Bounds.Width / 2, wave.Bounds.Height / 2), window)!.Value;

            window.MouseDown(at, MouseButton.Left);
            game.Controller.Refresh();
            Headless.Render();
            window.MouseUp(at, MouseButton.Left);
            Headless.Render();

            Assert.Equal(1, acted);
        });
    }

    private static readonly Color Amber = Color.Parse("#d97706");

    private static Color ColourOf(Control control) =>
        ((ISolidColorBrush)(control is Border border ? border.Background! : ((TemplatedControl)control).Background!)).Color;

    private static Canvas Map(Window window) => Headless.Shown<Canvas>(window).Single(canvas => canvas.Width == 100);

    private static Menus MenusOf(PanelGame game) =>
        new(game.Ana, DefaultNpcs.All, DefaultObjects.All, DefaultItems.All);

    private static void FillBank(PanelGame game)
    {
        var vault = game.Game.Server.Items.ContainerOf("ana", ContainerId.Bank)!;
        foreach (var item in DefaultItems.All.Items)
        {
            vault.Add(item.Id, 1, tab: 0);
        }
    }
}
