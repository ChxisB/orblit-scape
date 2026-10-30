using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Scape.Client;
using Scape.Client.Tests;
using Scape.Content;
using Scape.Core;

namespace Scape.Panels.Tests;

/// <summary>
/// A server with one player, Ana, her panels, and a way to draw them in a headless window. A test
/// that draws runs its body in <see cref="ShowAsync"/>, because controls belong to the UI thread.
/// </summary>
internal sealed class PanelGame : IAsyncDisposable
{
    public const double Height = 740;

    /// <summary>A tile next to where Ana spawns, for a creature, an object or a second player.</summary>
    public static readonly TileCoord Beside = new(33, 32, 0);

    private bool _drawn;

    private PanelGame(InProcessGame game, GameSession ana)
    {
        Game = game;
        Ana = ana;
        Controller = new PanelController(ana);
    }

    public InProcessGame Game { get; }

    public GameSession Ana { get; }

    public PanelController Controller { get; }

    /// <summary>Ana's inventory on the server, which the tests fill and empty.</summary>
    public Container Bag => Game.Server.Items.ContainerOf("ana", ContainerId.Inventory)!;

    public static async Task<PanelGame> StartAsync()
    {
        var game = new InProcessGame();
        return new PanelGame(game, await game.JoinAsync("Ana"));
    }

    /// <summary>Ana uses a bank booth beside her. The bank is open when this returns.</summary>
    public async Task OpenBankAsync()
    {
        var booth = (long)Game.Server.Objects.Place(ObjectId.BankBooth, Beside).NetworkId;
        await SettleAsync(2);
        Ana.UseObject(booth, 0);
        await SettleAsync(6);
    }

    /// <summary>Bo joins and walks up to Ana, and each asks the other to trade. Returns Bo.</summary>
    public async Task<GameSession> TradeWithBoAsync()
    {
        var bo = await Game.JoinAsync("Bo");
        bo.WalkTo(Beside);
        await SettleAsync(3);
        Ana.RequestTrade(InProcessGame.AvatarOf(bo));
        await SettleAsync(3);
        bo.RequestTrade(InProcessGame.AvatarOf(Ana));
        await SettleAsync(3);
        return bo;
    }

    /// <summary>Lets the server tick, then has the panels draw what it sent.</summary>
    public async Task SettleAsync(int ticks = 1)
    {
        await Game.PumpAsync();
        await Game.TickAndDrawAsync(Ana, ticks);
        Controller.Refresh();
        if (_drawn)
        {
            Headless.Render();
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> with Ana's panels in a window <paramref name="width"/> wide. The
    /// panels name items from <paramref name="items"/>, which is the game's own set unless a test says.
    /// </summary>
    public Task ShowAsync(Func<Window, Task> body, double width = 1280, ItemSet? items = null) =>
        Headless.Run(async () =>
        {
            var window = new Window
            {
                Width = width,
                Height = Height,
                Content = new ScapeShell(Controller, items ?? DefaultItems.All, DefaultSkills.All),
            };
            window.Show();
            _drawn = true;
            try
            {
                Headless.Render();
                await body(window);
            }
            finally
            {
                _drawn = false;
                window.Close();
            }
        });

    /// <summary>Everything written in the window, on a button or beside one.</summary>
    public static IReadOnlyList<string> Labels(Window window) =>
        [.. Headless.Shown<TextBlock>(window).Select(text => text.Text ?? "")];

    public static IReadOnlyList<Button> ButtonsSaying(Window window, string text) =>
        [.. Headless.Shown<Button>(window).Where(button => button.Content as string == text)];

    /// <summary>Presses the first button that says <paramref name="text"/>, as a pointer does.</summary>
    public static void Tap(Window window, string text) => Headless.Click(window, ButtonsSaying(window, text)[0]);

    /// <summary>The cell for a slot, which is a button that carries its slot.</summary>
    public static Button CellOf(Window window, ContainerId container, int slot) =>
        Headless.Shown<Button>(window).Single(button => button.Tag is Slot at && at == new Slot(container, slot));

    public static void TapCell(Window window, ContainerId container, int slot) =>
        Headless.Click(window, CellOf(window, container, slot));

    /// <summary>Where <paramref name="control"/>'s top left is in the window.</summary>
    public static Point TopLeft(Window window, Visual control) => control.TranslatePoint(new Point(0, 0), window)!.Value;

    public async ValueTask DisposeAsync() => await Game.DisposeAsync();
}
