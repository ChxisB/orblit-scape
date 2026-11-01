using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Orblit.Filament;
using Orblit.Filament.Avalonia;
using Orblit.Terrain;
using Scape.Client;
using Scape.Core;
using GroundKey = Orblit.Terrain.RegionKey;

namespace Scape.Demo;

/// <summary>
/// The island in 3D. A click walks and a shift-click runs. A right drag or the arrow keys turn
/// the camera and the wheel zooms. The text in the top left names what is under the pointer.
/// </summary>
internal sealed class IslandWindow : Window
{
    private const double TurnPerPixel = 0.008;
    private const double TurnPerKey = 0.08;
    private const double ZoomPerNotch = 0.9;

    private readonly Island _island;
    private readonly IReadOnlyList<RenderObject> _ground;
    private readonly ActorAnimator _animator = new();
    private readonly Terrain _flat = Flat();
    private readonly RendererViewport _viewport = new();
    private readonly TextBlock _hover = new() { FontSize = 13, Foreground = new ImmutableSolidColorBrush(Color.FromUInt32(0xFFF2E9D8)) };
    private readonly Border _label;
    private OrbitView _view;
    private Point? _dragFrom;
    private TimeSpan? _started;
    private double _tickAt;
    private int _loop;
    private bool _requested;

    public IslandWindow(Island island)
    {
        Title = "Scape island";
        Width = 1100;
        Height = 760;
        _island = island;
        _ground = IslandScene.Ground(island.Plan);
        _view = new OrbitView { Target = IslandScene.StandingAt(island.Player, 0), Yaw = Math.PI };

        _label = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(4),
            Background = new ImmutableSolidColorBrush(Color.FromUInt32(0xB0101418)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
            Child = _hover,
        };
        Content = new Panel { Children = { _viewport, _label } };

        _viewport.PointerPressed += OnPressed;
        _viewport.PointerMoved += OnMoved;
        _viewport.PointerReleased += (_, _) => _dragFrom = null;
        _viewport.PointerWheelChanged += (_, e) => _view = _view.Zoomed(Math.Pow(ZoomPerNotch, e.Delta.Y));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RequestFrame();
    }

    protected override void OnClosed(EventArgs e)
    {
        // A frame already asked for still comes after the window closes. The new loop leaves it unanswered.
        _loop++;
        _requested = false;
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        (double Yaw, double Pitch)? turn = e.Key switch
        {
            Key.Left => (-TurnPerKey, 0),
            Key.Right => (TurnPerKey, 0),
            Key.Up => (0, TurnPerKey),
            Key.Down => (0, -TurnPerKey),
            _ => null,
        };
        if (turn is { } by)
        {
            _view = _view.Turned(by.Yaw, by.Pitch);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_viewport);
        if (point.Properties.IsRightButtonPressed)
        {
            _dragFrom = point.Position;
        }
        else if (point.Properties.IsLeftButtonPressed && TileUnder(point.Position) is { } tile)
        {
            _island.WalkTo(tile, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? MoveSpeed.Run : MoveSpeed.Walk);
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var at = e.GetPosition(_viewport);
        if (_dragFrom is { } from)
        {
            _view = _view.Turned((at.X - from.X) * TurnPerPixel, (at.Y - from.Y) * TurnPerPixel);
            _dragFrom = at;
        }

        var text = TileUnder(at) is { } tile ? _island.Hover(tile) : "";
        _hover.Text = text;
        _label.IsVisible = text.Length > 0;
    }

    private TileCoord? TileUnder(Point at) =>
        TilePick.Pick(_flat, _view.RayThrough(at.X, at.Y, _viewport.Bounds.Width, _viewport.Bounds.Height));

    private void RequestFrame()
    {
        if (_requested)
        {
            return;
        }

        _requested = true;
        var loop = _loop;
        RequestAnimationFrame(now =>
        {
            if (loop == _loop)
            {
                OnFrame(now);
            }
        });
    }

    private void OnFrame(TimeSpan now)
    {
        _requested = false;
        _started ??= now;
        var seconds = (now - _started.Value).TotalSeconds;
        var period = GameTick.Period.TotalSeconds;

        // A tick runs on the first frame after its period, so ticks run late by a frame at most.
        if (seconds - _tickAt >= period)
        {
            _island.Tick();
            _tickAt = seconds;
        }

        var t = Math.Min(1, (seconds - _tickAt) / period);
        var objects = new List<RenderObject>(_ground);
        foreach (var actor in _island.Actors)
        {
            objects.AddRange(IslandScene.Posed(actor, _animator.Sample(actor.Id, new Anim(actor.State), seconds), t));
        }

        _view = _view.Following(IslandScene.StandingAt(_island.Player, t));
        _viewport.Frame(IslandScene.Of(objects, _view, _viewport.Bounds.Width / _viewport.Bounds.Height), seconds);
        RequestFrame();
    }

    // The island is flat, so a pick meets the ground at height 0 over the two regions it spans each way.
    private static Terrain Flat()
    {
        var terrain = new Terrain(regionSize: TileWorld.RegionSize);
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            terrain.FillHeights(new GroundKey(x, y), (_, _) => 0);
        }

        return terrain;
    }
}
