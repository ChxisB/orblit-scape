using Avalonia;
using Avalonia.Controls;

namespace Scape.Panels;

/// <summary>
/// The world and the side panel. From <see cref="WideFrom"/> wide, the side panel stands at the
/// right and is <see cref="SideWidth"/> wide for the full height. Narrower, it sits under the world
/// at the height its content needs, and the world takes what is left.
/// </summary>
/// <remarks>The first child is the world and the second is the side panel.</remarks>
// shortcut: it needs a window's bounded size. Under a parent that measures it without a limit, it asks for none.
// shortcut: a side panel taller than its window is cut off. Wrap it in a scroller that outlives a build, as the bank's does.
internal sealed class ShellLayout : Panel
{
    public const double WideFrom = 768;
    public const double SideWidth = 320;

    private Control World => Children[0];

    private Control Side => Children[1];

    protected override Size MeasureOverride(Size availableSize)
    {
        Side.Measure(IsWide(availableSize) ? new Size(SideWidth, availableSize.Height) : availableSize);
        World.Measure(Split(availableSize, Side.DesiredSize.Height).World.Size);
        return new Size(Finite(availableSize.Width), Finite(availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (world, side) = Split(finalSize, Side.DesiredSize.Height);
        World.Arrange(world);
        Side.Arrange(side);
        return finalSize;
    }

    private static bool IsWide(Size size) => size.Width >= WideFrom;

    private static (Rect World, Rect Side) Split(Size size, double sideHeight)
    {
        if (IsWide(size))
        {
            return (new Rect(0, 0, size.Width - SideWidth, size.Height), new Rect(size.Width - SideWidth, 0, SideWidth, size.Height));
        }

        var below = Math.Min(sideHeight, size.Height);
        return (new Rect(0, 0, size.Width, size.Height - below), new Rect(0, size.Height - below, size.Width, below));
    }

    private static double Finite(double length) => double.IsInfinity(length) ? 0 : length;
}
