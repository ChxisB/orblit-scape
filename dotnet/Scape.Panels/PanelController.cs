using Scape.Client;
using Scape.Core;

namespace Scape.Panels;

/// <summary>
/// Turns what a panel's buttons say into messages and into changes of <see cref="PanelState"/>. It
/// never changes what the server decides: a drag is sent and the slots change when the server's
/// answer arrives.
/// </summary>
public sealed class PanelController(GameSession session)
{
    public GameSession Session => session;

    public PanelState State { get; private set; } = new();

    /// <summary>Raised after every change of <see cref="State"/>, and by <see cref="Refresh"/>.</summary>
    public event Action? Changed;

    /// <summary>
    /// Tells the panels to redraw, after <see cref="GameSession.Advance"/>, because the session has no
    /// change signal of its own. A picked-up stack whose slot the server has emptied is put down.
    /// </summary>
    // shortcut: a pick stays on a slot the server has filled with another item. Hold the item beside the pick to drop it then.
    public void Refresh()
    {
        if (State.Picked is { } picked && session.ItemAt(picked.Container, picked.Index) is null)
        {
            State = State with { Picked = null };
        }

        Changed?.Invoke();
    }

    public void ShowTab(SideTab tab) => Set(State with { Tab = tab });

    public void ShowChat(ChatTab chat) => Set(State with { Chat = chat });

    /// <summary>Moves the volume by <paramref name="delta"/>, and stops at its ends.</summary>
    public void StepVolume(int delta) =>
        Set(State with { Volume = Math.Clamp(State.Volume + delta, 0, PanelState.MaxVolume) });

    /// <summary>Moves the view distance by <paramref name="delta"/>, and stops at its ends.</summary>
    public void StepViewDistance(int delta) =>
        Set(State with { ViewDistance = Math.Clamp(State.ViewDistance + delta, PanelState.MinViewDistance, PanelState.MaxViewDistance) });

    /// <summary>Shows <paramref name="entries"/> as the right-click menu, or hides it when empty.</summary>
    public void OpenMenu(IReadOnlyList<MenuEntry> entries) => Set(State with { Menu = entries });

    public void CloseMenu() => Set(State with { Menu = [] });

    /// <summary>Closes the menu, then runs the entry. An index with no entry only closes it.</summary>
    public void RunMenu(int index)
    {
        var entries = State.Menu;
        CloseMenu();
        if (index >= 0 && index < entries.Count)
        {
            entries[index].Run();
        }
    }

    /// <summary>Sets the text for what the pointer is over. The same text is no change.</summary>
    public void SetHover(string text)
    {
        if (text != State.Hover)
        {
            Set(State with { Hover = text });
        }
    }

    /// <summary>
    /// A tap on a slot. With a stack picked up it is put down there, and moved when that is another
    /// slot. With nothing picked up, an open bank takes one of the stack, an open trade offers one
    /// more of it, and otherwise it is picked up to be moved.
    /// </summary>
    public void TapSlot(ContainerId container, int index)
    {
        var here = new Slot(container, index);
        if (State.Picked is not { } picked)
        {
            PickOrAct(here);
            return;
        }

        Set(State with { Picked = null });
        if (picked != here)
        {
            session.MoveItem(picked.Container, picked.Index, container, index);
        }
    }

    private void PickOrAct(Slot here)
    {
        if (session.ItemAt(here.Container, here.Index) is not { } stack)
        {
            return;
        }

        var trade = session.PlayerTrade;
        if (here.Container == ContainerId.Inventory && session.IsBankOpen)
        {
            session.Deposit(here.Index, 1);
        }
        else if (here.Container == ContainerId.Inventory && trade is not null)
        {
            var offered = trade.Mine.Where(one => one.Item == stack.Item).Sum(one => (long)one.Count);
            session.Offer(stack.Item, offered + 1);
        }
        else
        {
            Set(State with { Picked = here });
        }
    }

    private void Set(PanelState next)
    {
        State = next;
        Changed?.Invoke();
    }
}
