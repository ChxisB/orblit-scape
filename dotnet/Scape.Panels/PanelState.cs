using Scape.Client;
using Scape.Core;

namespace Scape.Panels;

/// <summary>The tabs down the side of the screen.</summary>
public enum SideTab
{
    Inventory,
    Equipment,
    Skills,
    Quests,
    Friends,
    Settings,
}

/// <summary>One slot of one container.</summary>
public readonly record struct Slot(ContainerId Container, int Index);

/// <summary>
/// What the panels show that the server does not decide: the open tab, the stack picked up to move,
/// the menu and the two settings. A change is a copy, <c>state with { Tab = SideTab.Skills }</c>.
/// </summary>
public sealed record PanelState
{
    public const int MaxVolume = 10;
    public const int MinViewDistance = 4;
    public const int MaxViewDistance = 24;

    public SideTab Tab { get; init; } = SideTab.Inventory;

    public ChatTab Chat { get; init; } = ChatTab.Game;

    /// <summary>The stack the player tapped first, waiting for the slot to move it to.</summary>
    public Slot? Picked { get; init; }

    /// <summary>The right-click menu, or empty when none is open.</summary>
    public IReadOnlyList<MenuEntry> Menu
    {
        get;
        init => field = Array.AsReadOnly([.. value]);
    } = [];

    /// <summary>What the pointer is over, as text.</summary>
    public string Hover { get; init; } = "";

    public int Volume
    {
        get;
        init => field = Within(value, 0, MaxVolume, nameof(Volume));
    } = 5;

    public int ViewDistance
    {
        get;
        init => field = Within(value, MinViewDistance, MaxViewDistance, nameof(ViewDistance));
    } = 12;

    private static int Within(int value, int low, int high, string name) =>
        value >= low && value <= high
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"is outside {low} to {high}");
}
