using Scape.Client;
using Scape.Core;

namespace Scape.Panels.Tests;

public sealed class PanelStateTests
{
    [Fact]
    public void RefusesAVolumeOrViewDistanceOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState { Volume = PanelState.MaxVolume + 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState { Volume = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState { ViewDistance = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState { ViewDistance = PanelState.MaxViewDistance + 1 });
    }

    [Fact]
    public void TakesTheEndsOfBothSettings()
    {
        var low = new PanelState { Volume = 0, ViewDistance = PanelState.MinViewDistance };
        var high = new PanelState { Volume = PanelState.MaxVolume, ViewDistance = PanelState.MaxViewDistance };

        Assert.Equal((0, PanelState.MinViewDistance), (low.Volume, low.ViewDistance));
        Assert.Equal((PanelState.MaxVolume, PanelState.MaxViewDistance), (high.Volume, high.ViewDistance));
    }

    [Fact]
    public void RefusesACopyOutOfRangeToo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState() with { Volume = 11 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PanelState() with { ViewDistance = 25 });
    }

    [Fact]
    public void StartsOnTheInventoryWithNothingPickedOrOpen()
    {
        var state = new PanelState();

        Assert.Equal(SideTab.Inventory, state.Tab);
        Assert.Equal(ChatTab.Game, state.Chat);
        Assert.Null(state.Picked);
        Assert.Empty(state.Menu);
        Assert.Equal("", state.Hover);
        Assert.Equal((5, 12), (state.Volume, state.ViewDistance));
    }

    [Fact]
    public void PutsAPickedStackDownWhenAskedForNull()
    {
        var held = new PanelState { Picked = new Slot(ContainerId.Inventory, 2) };

        Assert.NotNull((held with { Tab = SideTab.Skills }).Picked);
        Assert.Null((held with { Picked = null }).Picked);
    }

    [Fact]
    public void KeepsItsOwnCopyOfTheMenu()
    {
        var entries = new List<MenuEntry> { new("Take Logs", () => { }) };
        var state = new PanelState { Menu = entries };

        entries.Add(new MenuEntry("Cancel", () => { }));

        Assert.Equal(["Take Logs"], state.Menu.Select(entry => entry.Label));
        Assert.Throws<NotSupportedException>(() => ((IList<MenuEntry>)state.Menu).Add(new MenuEntry("Cancel", () => { })));
    }
}
