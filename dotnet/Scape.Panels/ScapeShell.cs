using Avalonia;
using Avalonia.Controls;
using Scape.Core;

namespace Scape.Panels;

/// <summary>
/// The scape's panels over a <see cref="PanelController"/>: the world's hover text, minimap and
/// right-click menu, and the side panel with its tabs, windows and chat. It draws again on each
/// change of the controller while it is on screen, and holds nothing while it is not.
/// </summary>
public sealed class ScapeShell : UserControl
{
    private readonly PanelController _controller;
    private readonly Panels _panels;

    public ScapeShell(PanelController controller, ItemSet items, SkillSet skills)
    {
        _controller = controller;
        _panels = new Panels(controller, items, skills);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _controller.Changed += Redraw;
        Redraw();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _controller.Changed -= Redraw;
        base.OnDetachedFromVisualTree(e);
    }

    private void Redraw() => Content = _panels.Build();
}
