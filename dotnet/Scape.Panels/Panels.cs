using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Scape.Client;
using Scape.Core;

namespace Scape.Panels;

/// <summary>
/// The panels as one tree of controls. It reads the session and the state and changes neither, so
/// the same inputs give the same tree. A button acts when it is pressed, because the tree is built
/// again on each tick and a button can be gone before it is let go.
/// </summary>
// shortcut: a redraw drops keyboard focus. Update the controls in place when the panels need the keyboard.
internal sealed class Panels(PanelController controller, ItemSet items, SkillSet skills)
{
    private const int Columns = 4;
    private const int ChatLines = 6;
    private const int MapRadius = 16;
    private const double MapSize = 100;
    private const double DotSize = 4;
    private const double Small = 12;

    private static readonly IBrush Slate900 = Brush.Parse("#0f172a");
    private static readonly IBrush Slate800 = Brush.Parse("#1e293b");
    private static readonly IBrush Slate700 = Brush.Parse("#334155");
    private static readonly IBrush Slate500 = Brush.Parse("#64748b");
    private static readonly IBrush Slate100 = Brush.Parse("#f1f5f9");
    private static readonly IBrush Amber600 = Brush.Parse("#d97706");
    private static readonly IBrush NpcDot = Brush.Parse("#facc15");
    private static readonly IBrush PlayerDot = Brush.Parse("#38bdf8");
    private static readonly IBrush SelfDot = Brush.Parse("#ffffff");

    // The bank's scroller and its list outlive a build, because a new child resets the scroll offset.
    private readonly StackPanel _bankList = new() { Spacing = 4 };
    private readonly ScrollViewer _bankScroll = new() { Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    private GameSession Session => controller.Session;

    private PanelState State => controller.State;

    /// <summary>A bank and a trade are fed by taps on the inventory, so it stays on screen under their window.</summary>
    private bool TakesInventoryTaps => Session.IsBankOpen || Session.PlayerTrade is not null;

    public ShellLayout Build() => new() { Background = Slate900, Children = { World(), Side() } };

    private Grid World()
    {
        var world = new Grid { Margin = new Thickness(8) };
        world.Children.Add(new TextBlock { Text = State.Hover, Foreground = Slate100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        world.Children.Add(MapView());
        if (State.Menu.Count > 0)
        {
            world.Children.Add(Menu(State.Menu));
        }

        return world;
    }

    // shortcut: the menu opens at a fixed place and only Cancel closes it. Open it at the pointer when the world gives one.
    private Border Menu(IReadOnlyList<MenuEntry> entries)
    {
        var column = Column();
        foreach (var (entry, index) in entries.Select((entry, index) => (entry, index)))
        {
            column.Children.Add(Press(entry.Label, () => controller.RunMenu(index)));
        }

        column.Children.Add(Press("Cancel", controller.CloseMenu));
        return new Border
        {
            Margin = new Thickness(40, 40, 0, 0),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Background = Slate800,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = column,
        };
    }

    private Canvas MapView()
    {
        const double centre = MapSize / 2;
        const double scale = MapSize / (MapRadius * 2);
        var map = new Canvas
        {
            Width = MapSize,
            Height = MapSize,
            Background = Slate800,
            Margin = new Thickness(0, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        foreach (var (dx, dy, kind) in Minimap.Dots(Session, MapRadius))
        {
            map.Children.Add(DotAt(centre + dx * scale, centre - dy * scale, kind == DotKind.Npc ? NpcDot : PlayerDot));
        }

        map.Children.Add(DotAt(centre, centre, SelfDot));
        return map;
    }

    /// <summary>A dot centred on (<paramref name="x"/>, <paramref name="y"/>) that stays inside the map.</summary>
    private static Border DotAt(double x, double y, IBrush colour)
    {
        var dot = new Border { Width = DotSize, Height = DotSize, Background = colour };
        Canvas.SetLeft(dot, Math.Clamp(x - DotSize / 2, 0, MapSize - DotSize));
        Canvas.SetTop(dot, Math.Clamp(y - DotSize / 2, 0, MapSize - DotSize));
        return dot;
    }

    private Border Side()
    {
        var column = Column(Tabs(), OpenWindow() ?? Body());
        if (TakesInventoryTaps)
        {
            column.Children.Add(Slots(ContainerId.Inventory));
        }

        column.Children.Add(Chat());
        return new Border { Background = Slate800, Padding = new Thickness(8), Child = column };
    }

    private UniformGrid Tabs()
    {
        var tabs = new UniformGrid { Rows = 1 };
        foreach (var tab in Enum.GetValues<SideTab>())
        {
            var button = Press(tab.ToString().ToLowerInvariant()[..3], () => controller.ShowTab(tab));
            button.Background = tab == State.Tab ? Slate500 : Slate700;
            tabs.Children.Add(button);
        }

        return tabs;
    }

    private Control Body() => State.Tab switch
    {
        SideTab.Inventory => Slots(ContainerId.Inventory),
        SideTab.Equipment => Gear(),
        SideTab.Skills => SkillList(),
        SideTab.Quests => Quests(),
        SideTab.Friends => Friends(),
        _ => Settings(),
    };

    private UniformGrid Slots(ContainerId container)
    {
        var grid = new UniformGrid { Columns = Columns };
        for (var slot = 0; slot < Container.SizeOf(container); slot++)
        {
            grid.Children.Add(Cell(container, slot));
        }

        return grid;
    }

    // shortcut: a name wider than its cell is cut off. Put it in a wrapping TextBlock when the names need it.
    private Button Cell(ContainerId container, int slot)
    {
        var here = new Slot(container, slot);
        var cell = Press(Session.ItemAt(container, slot) is { } stack ? StackLabel(stack) : "", () => controller.TapSlot(container, slot));
        cell.Height = 40;
        cell.FontSize = Small;
        cell.Margin = new Thickness(2);
        cell.Padding = new Thickness(2, 4);
        cell.Background = State.Picked == here ? Amber600 : Slate700;
        cell.Tag = here;
        return cell;
    }

    private string StackLabel(ItemStack stack)
    {
        var name = NameOf(stack.Item);
        return stack.Count > 1 ? $"{name} x{stack.Count}" : name;
    }

    /// <summary>A client with older content can be sent an item it has no name for.</summary>
    private string NameOf(int item) => items.Find(item)?.Name ?? $"Item {item}";

    private StackPanel Gear()
    {
        var column = Column();
        for (var slot = 0; slot < ItemDef.EquipSlots.Count; slot++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };
            row.Children.Add(Text(ItemDef.EquipSlots[slot], Small));
            var cell = Cell(ContainerId.Equipment, slot);
            Grid.SetColumn(cell, 1);
            row.Children.Add(cell);
            column.Children.Add(row);
        }

        return column;
    }

    private StackPanel SkillList()
    {
        var column = Column();
        foreach (var skill in skills.Skills)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(Text(skill.Name));
            var level = Text(Session.LevelOf(skill.Id).ToString(CultureInfo.InvariantCulture));
            Grid.SetColumn(level, 1);
            row.Children.Add(level);
            column.Children.Add(row);
        }

        return column;
    }

    private StackPanel Quests()
    {
        var journal = Session.Journal;
        if (journal.Count == 0)
        {
            return Column(Text("No quests begun."));
        }

        var column = Column();
        foreach (var (title, text, done) in journal)
        {
            column.Children.Add(Column(Text(done ? $"{title} (done)" : title, weight: FontWeight.Bold), Text(text, Small)));
        }

        return column;
    }

    private StackPanel Friends()
    {
        var column = Column();
        foreach (var (name, online) in Session.Friends)
        {
            column.Children.Add(Text(online ? $"{name} (online)" : name));
        }

        if (Session.Friends.Count == 0)
        {
            column.Children.Add(Text("No friends added."));
        }

        foreach (var name in Session.Ignored)
        {
            column.Children.Add(Text($"{name} (ignored)", Small));
        }

        return column;
    }

    private StackPanel Settings() => Column(
        Stepper("Volume", State.Volume, controller.StepVolume),
        Stepper("View distance", State.ViewDistance, controller.StepViewDistance));

    private static Grid Stepper(string label, int value, Action<int> step)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,32,Auto") };
        var minus = Press("-", () => step(-1));
        var number = Text(value.ToString(CultureInfo.InvariantCulture));
        var plus = Press("+", () => step(1));
        number.TextAlignment = TextAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(minus, 1);
        Grid.SetColumn(number, 2);
        Grid.SetColumn(plus, 3);
        row.Children.Add(Text(label));
        row.Children.AddRange([minus, number, plus]);
        return row;
    }

    private Border Chat()
    {
        var tabs = new UniformGrid { Rows = 1 };
        foreach (var tab in Enum.GetValues<ChatTab>())
        {
            var button = Press(tab.ToString().ToLowerInvariant(), () => controller.ShowChat(tab));
            button.FontSize = Small;
            button.Background = tab == State.Chat ? Slate500 : Slate700;
            tabs.Children.Add(button);
        }

        var column = Column(tabs);
        foreach (var line in Session.ChatOf(State.Chat).TakeLast(ChatLines))
        {
            column.Children.Add(Text(line, Small));
        }

        return new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(4), Background = Slate900, Child = column };
    }

    /// <summary>The window the server has open, or null. A trade, a shop, a bank and a conversation are never open together.</summary>
    private StackPanel? OpenWindow()
    {
        if (Session.PlayerTrade is { } trade)
        {
            return Trade(trade);
        }

        if (Session.Shop is { } shop)
        {
            return Shop(shop);
        }

        if (Session.IsBankOpen)
        {
            return Bank();
        }

        if (Session.Line is { } line)
        {
            return Column(Text(line.Speaker, weight: FontWeight.Bold), Text(line.Text), Press("Continue", Session.ContinueTalk));
        }

        return Session.Choices.Count > 0 ? Choices() : null;
    }

    private StackPanel Trade(TradeView trade)
    {
        var buttons = new UniformGrid { Rows = 1 };
        buttons.Children.Add(Press(trade.Step.Confirming ? "Confirm" : "Accept", Session.AcceptTrade));
        buttons.Children.Add(Press("Decline", Session.DeclineTrade));
        return Column(
            Text($"Trading with {trade.Partner}", weight: FontWeight.Bold),
            Text($"You offer: {Stacks(trade.Mine)}", Small),
            Text($"They offer: {Stacks(trade.Theirs)}", Small),
            buttons);
    }

    private string Stacks(IReadOnlyList<ItemStack> stacks) =>
        stacks.Count == 0 ? "nothing" : string.Join(", ", stacks.Select(StackLabel));

    private StackPanel Shop(ShopView shop)
    {
        var column = Column(Text("Shop", weight: FontWeight.Bold));
        foreach (var (item, count) in shop.Lines)
        {
            column.Children.Add(Press($"{NameOf(item)} ({count} left)", () => Session.Buy(item, 1), HorizontalAlignment.Left));
        }

        return column;
    }

    // shortcut: the first bank tab only. Add a tab strip when a bank needs it.
    // F5: a build reuses the scroller and its list, and so changes them. A new child would reset the offset.
    private StackPanel Bank()
    {
        _bankList.Children.Clear();
        foreach (var slot in Enumerable.Range(0, Container.BankTabSize))
        {
            if (Session.ItemAt(ContainerId.Bank, slot) is { } stack)
            {
                _bankList.Children.Add(Press(StackLabel(stack), () => Session.Withdraw(slot, 1), HorizontalAlignment.Left));
            }
        }

        (_bankScroll.Parent as Panel)?.Children.Remove(_bankScroll);
        _bankScroll.Content ??= _bankList;
        return Column(Text("Bank", weight: FontWeight.Bold), _bankScroll);
    }

    private StackPanel Choices()
    {
        var column = Column();
        foreach (var (choice, index) in Session.Choices.Select((choice, index) => (choice, index)))
        {
            column.Children.Add(Press(choice, () => Session.Choose(index), HorizontalAlignment.Left));
        }

        return column;
    }

    private static StackPanel Column(params IEnumerable<Control> children)
    {
        var column = new StackPanel { Spacing = 4 };
        column.Children.AddRange(children);
        return column;
    }

    private static TextBlock Text(string text, double size = 14, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = Slate100,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button Press(string label, Action act, HorizontalAlignment content = HorizontalAlignment.Center)
    {
        var button = new Button
        {
            Content = label,
            ClickMode = ClickMode.Press,
            Background = Slate700,
            Foreground = Slate100,
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = content,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => act();
        return button;
    }
}
