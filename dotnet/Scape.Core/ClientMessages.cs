using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Scape.Core;

/// <summary>A message a client sends the server.</summary>
public abstract class ClientMessage
{
    // Closed to this assembly, so the cases below are every message.
    private protected ClientMessage()
    {
    }
}

/// <summary>
/// The credentials in a join. A server with accounts checks <see cref="Password"/> against
/// the account named <see cref="Name"/>, or makes the account when <see cref="Create"/> is set.
/// A server without accounts reads only <see cref="Name"/>.
/// </summary>
public sealed class Login : ClientMessage
{
    public Login(string name, string password = "", bool create = false)
    {
        if (!Wire.IsValidName(name))
        {
            throw new ArgumentException($"{name} is not a valid name.", nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(Encoding.UTF8.GetByteCount(password), Wire.MaxPasswordBytes, nameof(password));
        Name = name;
        Password = password;
        Create = create;
    }

    public string Name { get; }

    public string Password { get; }

    public bool Create { get; }
}

/// <summary>What a player asks to do. The server turns it into an action.</summary>
public abstract class Intent : ClientMessage
{
    private protected Intent()
    {
    }
}

public sealed class Walk(TileCoord target) : Intent
{
    public TileCoord Target { get; } = target;
}

/// <summary>
/// Use the thing <see cref="Id"/> names, in the way <see cref="Option"/> picks. What
/// <see cref="Kind"/> and <see cref="Option"/> mean belongs to the interactions that read them.
/// </summary>
public sealed class Interact : Intent
{
    public Interact(int kind, long id, int option)
    {
        Wire.InRange(kind, 0xFF);
        Wire.InRange(id, 0xFFFFFFFF);
        Wire.InRange(option, 0xFF);
        Kind = kind;
        Id = id;
        Option = option;
    }

    public int Kind { get; }

    public long Id { get; }

    public int Option { get; }
}

public sealed class Chat : Intent
{
    public Chat(string text)
    {
        if (!Wire.IsValidText(text))
        {
            throw new ArgumentException($"{text} is not valid chat text.", nameof(text));
        }

        Text = text;
    }

    public string Text { get; }
}

/// <summary>A private message to the player called <see cref="To"/>, wherever they are.</summary>
public sealed class Whisper : Intent
{
    public Whisper(string to, string text)
    {
        if (!Wire.IsValidName(to))
        {
            throw new ArgumentException($"{to} is not a valid name.", nameof(to));
        }

        if (!Wire.IsValidText(text))
        {
            throw new ArgumentException($"{text} is not valid chat text.", nameof(text));
        }

        To = to;
        Text = text;
    }

    public string To { get; }

    public string Text { get; }
}

/// <summary>The two lists a player keeps of other players.</summary>
public enum ListKind
{
    Friends,
    Ignored,
}

/// <summary>Puts <see cref="Name"/> on a list, or takes it off.</summary>
public sealed class EditList : Intent
{
    public EditList(ListKind list, bool add, string name)
    {
        if (!Enum.IsDefined(list))
        {
            throw new ArgumentOutOfRangeException(nameof(list), list, "is not a list.");
        }

        if (!Wire.IsValidName(name))
        {
            throw new ArgumentException($"{name} is not a valid name.", nameof(name));
        }

        List = list;
        Add = add;
        Name = name;
    }

    public ListKind List { get; }

    public bool Add { get; }

    public string Name { get; }
}

/// <summary>An item and a count, which a trade with a shop names.</summary>
public abstract class Trade : Intent
{
    private protected Trade(int item, long count)
    {
        Wire.CheckItem(item);
        Wire.CheckCount(count);
        Item = item;
        Count = count;
    }

    public int Item { get; }

    public long Count { get; }
}

/// <summary>Buy <see cref="Trade.Count"/> of <see cref="Trade.Item"/> from the shop the player has open.</summary>
public sealed class Buy(int item, long count) : Trade(item, count);

/// <summary>Sell <see cref="Trade.Count"/> of <see cref="Trade.Item"/> to the shop the player has open.</summary>
public sealed class Sell(int item, long count) : Trade(item, count);

/// <summary>
/// Put up to <see cref="Count"/> of the inventory stack in <see cref="Slot"/> into the bank,
/// in bank tab <see cref="Tab"/>.
/// </summary>
public sealed class Deposit : Intent
{
    public Deposit(int slot, long count, int tab)
    {
        Wire.InRange(slot, 0xFFFF);
        Wire.CheckCount(count);
        Wire.InRange(tab, 0xFF);
        Slot = slot;
        Count = count;
        Tab = tab;
    }

    public int Slot { get; }

    public long Count { get; }

    public int Tab { get; }
}

/// <summary>Take up to <see cref="Count"/> of the bank stack in <see cref="Slot"/> into the inventory.</summary>
public sealed class Withdraw : Intent
{
    public Withdraw(int slot, long count)
    {
        Wire.InRange(slot, 0xFFFF);
        Wire.CheckCount(count);
        Slot = slot;
        Count = count;
    }

    public int Slot { get; }

    public long Count { get; }
}

/// <summary>
/// Swap the stack in <see cref="FromSlot"/> of <see cref="From"/> with <see cref="ToSlot"/> of
/// <see cref="To"/>. Dragging an item to an empty slot, to another stack or onto a worn slot is
/// this. The bank has its own messages.
/// </summary>
public sealed class MoveItem : Intent
{
    public MoveItem(ContainerId from, int fromSlot, ContainerId to, int toSlot)
    {
        Wire.InRange(fromSlot, 0xFFFF);
        Wire.InRange(toSlot, 0xFFFF);
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to) || from == ContainerId.Bank || to == ContainerId.Bank)
        {
            throw new ArgumentException("MoveItem moves between the inventory and the equipment.");
        }

        From = from;
        FromSlot = fromSlot;
        To = to;
        ToSlot = toSlot;
    }

    public ContainerId From { get; }

    public int FromSlot { get; }

    public ContainerId To { get; }

    public int ToSlot { get; }
}

/// <summary>
/// Offer <see cref="Count"/> of <see cref="Item"/> in the trade the player has open, in place
/// of what they offered of it. A count of zero takes it back. Any change sets both players'
/// accepts back.
/// </summary>
public sealed class TradeOffer : Intent
{
    public TradeOffer(int item, long count)
    {
        Wire.CheckItem(item);
        Wire.InRange(count, ItemStack.MaxCount);
        Item = item;
        Count = count;
    }

    public int Item { get; }

    public long Count { get; }
}

/// <summary>
/// Agree to the trade as it stands. The first agreement of both players moves the trade to its
/// confirm step. The second one makes the swap.
/// </summary>
public sealed class TradeAccept : Intent;

/// <summary>Leave the trade the player has open. Nothing changes hands.</summary>
public sealed class TradeDecline : Intent;

/// <summary>
/// Cast the spell <see cref="Spell"/> names at the creature <see cref="Target"/> names. A spell
/// that works on the caster, such as a teleport, ignores <see cref="Target"/>.
/// </summary>
public sealed class Cast : Intent
{
    public Cast(int spell, long target)
    {
        Wire.InRange(spell, SpellDef.MaxId);
        Wire.InRange(target, 0xFFFFFFFF);
        Spell = spell;
        Target = target;
    }

    public int Spell { get; }

    public long Target { get; }
}

/// <summary>
/// Switch the prayer <see cref="Prayer"/> names on, or off if it is on. A prayer that raises the
/// same stat goes off when this one comes on.
/// </summary>
public sealed class TogglePrayer : Intent
{
    public TogglePrayer(int prayer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(prayer, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prayer, PrayerDef.MaxId);
        Prayer = prayer;
    }

    public int Prayer { get; }
}

/// <summary>Go on from the line a conversation shows.</summary>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The game's term. Scape is called only from C#.")]
public sealed class Continue : Intent;

/// <summary>Pick the option at <see cref="Index"/> of the choices a conversation last offered.</summary>
public sealed class Choose : Intent
{
    public Choose(int index)
    {
        Wire.InRange(index, 0xFF);
        Index = index;
    }

    public int Index { get; }
}
