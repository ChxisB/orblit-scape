namespace Scape.Core;

/// <summary>The containers a player owns. The wire names one by its index.</summary>
public enum ContainerId
{
    Inventory,
    Equipment,
    Bank,
}

/// <summary>
/// How an operation ended. A refusal is a value, never an exception, because
/// a full bag is a normal thing for a player to meet.
/// </summary>
public enum Outcome
{
    Done,
    Partly,
    NothingThere,
    NoSuchSlot,
    WontFit,
    NotAllowed,
}

/// <summary>What a slot holds now. A null stack is an empty slot.</summary>
public readonly record struct SlotChange(int Slot, ItemStack? Stack);

/// <summary>
/// A fixed row of slots, each empty or holding a stack.
/// Each operation is atomic: it moves everything it reports or nothing. The
/// slots an operation changed are listed in <see cref="Dirty"/> until <see cref="MarkClean"/>,
/// so the server can send a change once a tick.
/// </summary>
// F5: each command returns its outcome, since a refusal is not an error.
public sealed class Container
{
    /// <summary>Slots in an inventory.</summary>
    public const int InventorySize = 28;

    /// <summary>Tabs in a bank.</summary>
    public const int BankTabs = 8;

    /// <summary>Slots in each bank tab.</summary>
    public const int BankTabSize = 100;

    private readonly ItemSet _items;
    private readonly Func<int, ItemDef, bool>? _accepts;
    private readonly ItemStack?[] _slots;
    private readonly SortedSet<int> _dirty = [];

    /// <summary>
    /// A container of <paramref name="size"/> slots. <paramref name="accepts"/> says whether
    /// an item may sit in a slot, and any item may sit anywhere without it.
    /// </summary>
    public Container(ItemSet items, int size, Func<int, ItemDef, bool>? accepts = null, int? tabSize = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        TabSize = tabSize ?? size;
        if (TabSize < 1 || size % TabSize != 0)
        {
            throw new ArgumentException(FormattableString.Invariant($"The tab size {tabSize} does not divide the size."), nameof(tabSize));
        }

        _items = items;
        _accepts = accepts;
        _slots = new ItemStack?[size];
    }

    /// <summary>Slots in each tab. A container without tabs is one tab.</summary>
    public int TabSize { get; }

    public int Size => _slots.Length;

    /// <summary>The stack in each slot, an empty slot as null.</summary>
    public IReadOnlyList<ItemStack?> Slots => Array.AsReadOnly([.. _slots]);

    /// <summary>The slots changed since the last <see cref="MarkClean"/>, lowest first.</summary>
    public IReadOnlyList<int> Dirty => [.. _dirty];

    /// <summary>How many slots the container for <paramref name="id"/> has.</summary>
    public static int SizeOf(ContainerId id) => id switch
    {
        ContainerId.Inventory => InventorySize,
        ContainerId.Equipment => ItemDef.EquipSlots.Count,
        ContainerId.Bank => BankTabs * BankTabSize,
        _ => throw new InvalidOperationException($"{id} is not a container."),
    };

    /// <summary>The container a player owns for <paramref name="id"/>.</summary>
    public static Container ForId(ContainerId id, ItemSet items) => id switch
    {
        ContainerId.Inventory => new Container(items, InventorySize),
        ContainerId.Equipment => new Container(items, SizeOf(id), (slot, item) => item.EquipSlot == ItemDef.EquipSlots[slot]),
        ContainerId.Bank => new Container(items, SizeOf(id), tabSize: BankTabSize),
        _ => throw new InvalidOperationException($"{id} is not a container."),
    };

    public void MarkClean() => _dirty.Clear();

    /// <summary>The stack in <paramref name="slot"/>, or null when it is empty or there is no such slot.</summary>
    public ItemStack? At(int slot) => Has(slot) ? _slots[slot] : null;

    /// <summary>The total of <paramref name="item"/> over every slot. A long, because full stacks add past an int.</summary>
    public long CountOf(int item) => _slots.Sum(stack => stack is { } held && held.Item == item ? held.Count : 0L);

    /// <summary>The slots that hold something, in <paramref name="tab"/> or everywhere when it is null.</summary>
    public IReadOnlyList<int> Occupied(int? tab = null) => [.. TabSlots(tab).Where(slot => _slots[slot] is not null)];

    /// <summary>The occupied slots whose item name holds <paramref name="text"/>, whatever the case.</summary>
    public IReadOnlyList<int> Search(string text, int? tab = null) =>
        [.. Occupied(tab).Where(slot => _items.Require(_slots[slot]!.Value.Item).Name.Contains(text, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Whether one more of <paramref name="item"/> would go in. Throws
    /// <see cref="ArgumentException"/> for an item that does not exist.
    /// </summary>
    public bool CanHold(int item)
    {
        var def = _items.Require(item);
        for (var slot = 0; slot < Size; slot++)
        {
            var room = _slots[slot] is { } stack
                ? def.Stackable && stack.Item == item && stack.Count < ItemStack.MaxCount
                : Allows(slot, item);
            if (room)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Puts up to <paramref name="count"/> of <paramref name="item"/> in, into <paramref name="tab"/>
    /// or anywhere when it is null, and returns how many went in. A stackable item fills its own
    /// stacks first. Throws <see cref="ArgumentException"/> for an item that does not exist and
    /// <see cref="ArgumentOutOfRangeException"/> for a count below one.
    /// </summary>
    public long Add(int item, long count, int? tab = null)
    {
        var def = _items.Require(item);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var slots = TabSlots(tab);
        var left = count;
        if (def.Stackable)
        {
            left -= TopUp(item, left, slots);
        }

        left -= FillEmpty(def, left, slots);
        return count - left;
    }

    /// <summary>
    /// Takes up to <paramref name="count"/> out of <paramref name="slot"/>, or all of it, and
    /// returns what came out. Null when there is nothing to take.
    /// </summary>
    public ItemStack? Take(int slot, int? count = null)
    {
        if (At(slot) is not { } stack)
        {
            return null;
        }

        var taken = Math.Min(count ?? stack.Count, stack.Count);
        if (taken < 1)
        {
            return null;
        }

        Set(slot, taken == stack.Count ? null : new ItemStack(stack.Item, stack.Count - taken));
        return new ItemStack(stack.Item, taken);
    }

    /// <summary>Moves the stack in <paramref name="from"/> to the empty slot <paramref name="to"/>.</summary>
    public Outcome Move(int from, int to)
    {
        if (!Has(from) || !Has(to))
        {
            return Outcome.NoSuchSlot;
        }

        if (_slots[from] is not { } stack)
        {
            return Outcome.NothingThere;
        }

        if (_slots[to] is not null)
        {
            return Outcome.WontFit;
        }

        if (!Allows(to, stack.Item))
        {
            return Outcome.NotAllowed;
        }

        Set(to, stack);
        Set(from, null);
        return Outcome.Done;
    }

    /// <summary>Exchanges two slots. One of them may be empty.</summary>
    public Outcome Swap(int a, int b)
    {
        if (!Has(a) || !Has(b))
        {
            return Outcome.NoSuchSlot;
        }

        var first = _slots[a];
        var second = _slots[b];
        if (first is null && second is null)
        {
            return Outcome.NothingThere;
        }

        if (a == b)
        {
            return Outcome.Done;
        }

        if (!AllowsOrEmpty(b, first) || !AllowsOrEmpty(a, second))
        {
            return Outcome.NotAllowed;
        }

        Set(a, second);
        Set(b, first);
        return Outcome.Done;
    }

    /// <summary>
    /// Exchanges <paramref name="slot"/> with <paramref name="otherSlot"/> of <paramref name="other"/>.
    /// One of them may be empty. Each item must be allowed where it lands. In the same container
    /// this is <see cref="Swap"/>.
    /// </summary>
    public Outcome SwapWith(Container other, int slot, int otherSlot)
    {
        if (ReferenceEquals(this, other))
        {
            return Swap(slot, otherSlot);
        }

        if (!Has(slot) || !other.Has(otherSlot))
        {
            return Outcome.NoSuchSlot;
        }

        var mine = _slots[slot];
        var theirs = other._slots[otherSlot];
        if (mine is null && theirs is null)
        {
            return Outcome.NothingThere;
        }

        if (!other.AllowsOrEmpty(otherSlot, mine) || !AllowsOrEmpty(slot, theirs))
        {
            return Outcome.NotAllowed;
        }

        Set(slot, theirs);
        other.Set(otherSlot, mine);
        return Outcome.Done;
    }

    /// <summary>
    /// Moves as much of the stack in <paramref name="from"/> onto the same item in
    /// <paramref name="to"/> as fits under <see cref="ItemStack.MaxCount"/>.
    /// </summary>
    public Outcome Stack(int from, int to)
    {
        if (!Has(from) || !Has(to))
        {
            return Outcome.NoSuchSlot;
        }

        if (_slots[from] is not { } source || _slots[to] is not { } target)
        {
            return Outcome.NothingThere;
        }

        var moved = from != to ? Movable(source, target) : 0;
        if (moved == 0)
        {
            return Outcome.WontFit;
        }

        Set(to, new ItemStack(target.Item, target.Count + moved));
        Set(from, moved == source.Count ? null : new ItemStack(source.Item, source.Count - moved));
        return moved == source.Count ? Outcome.Done : Outcome.Partly;
    }

    /// <summary>
    /// Moves <paramref name="count"/> from the stack in <paramref name="from"/> to the empty slot
    /// <paramref name="to"/>, leaving the rest. <paramref name="count"/> must be below the stack,
    /// or the whole move is <see cref="Move"/>.
    /// </summary>
    public Outcome Split(int from, int to, int count)
    {
        if (!Has(from) || !Has(to))
        {
            return Outcome.NoSuchSlot;
        }

        if (_slots[from] is not { } stack)
        {
            return Outcome.NothingThere;
        }

        if (count < 1 || count >= stack.Count || _slots[to] is not null)
        {
            return Outcome.WontFit;
        }

        if (!Allows(to, stack.Item))
        {
            return Outcome.NotAllowed;
        }

        Set(to, new ItemStack(stack.Item, count));
        Set(from, new ItemStack(stack.Item, stack.Count - count));
        return Outcome.Done;
    }

    /// <summary>
    /// Moves up to <paramref name="count"/> of the stack in <paramref name="slot"/> into
    /// <paramref name="into"/>, at <paramref name="tab"/> or anywhere. A deposit and a withdrawal
    /// are this call with the containers the other way round. What does not fit stays in
    /// <paramref name="slot"/>.
    /// </summary>
    public Outcome Transfer(int slot, Container into, int? count = null, int? tab = null)
    {
        if (ReferenceEquals(this, into))
        {
            return Outcome.NotAllowed;
        }

        if (!Has(slot))
        {
            return Outcome.NoSuchSlot;
        }

        if (_slots[slot] is not { } stack)
        {
            return Outcome.NothingThere;
        }

        var want = Math.Min(count ?? stack.Count, stack.Count);
        if (want < 1)
        {
            return Outcome.WontFit;
        }

        // Never more than want, so it fits an int.
        var added = (int)into.Add(stack.Item, want, tab);
        if (added == 0)
        {
            return Outcome.WontFit;
        }

        Take(slot, added);
        return added == want ? Outcome.Done : Outcome.Partly;
    }

    /// <summary>
    /// Takes <paramref name="take"/> out and puts <paramref name="give"/> in as one change. When
    /// <paramref name="take"/> is not all held, or <paramref name="give"/> does not all fit,
    /// nothing changes and no slot is marked dirty. What <paramref name="take"/> frees is there
    /// for <paramref name="give"/> to use. Throws <see cref="ArgumentException"/> for an item
    /// that does not exist.
    /// </summary>
    public Outcome Exchange(IReadOnlyList<ItemStack>? take = null, IReadOnlyList<ItemStack>? give = null)
    {
        take ??= [];
        give ??= [];
        foreach (var stack in take.Concat(give))
        {
            _items.Require(stack.Item);
        }

        ItemStack?[] before = [.. _slots];
        SortedSet<int> dirtyBefore = [.. _dirty];
        var outcome = ExchangeAll(take, give);
        if (outcome != Outcome.Done)
        {
            before.CopyTo(_slots, 0);
            _dirty.Clear();
            _dirty.UnionWith(dirtyBefore);
        }

        return outcome;
    }

    /// <summary>
    /// What <see cref="Exchange"/> with the same arguments would answer. Nothing changes,
    /// and nothing is marked dirty.
    /// </summary>
    public Outcome TryExchange(IReadOnlyList<ItemStack>? take = null, IReadOnlyList<ItemStack>? give = null)
    {
        var probe = new Container(_items, Size, _accepts, TabSize);
        probe.Restore(_slots);
        return probe.Exchange(take, give);
    }

    /// <summary>
    /// Sets slots to what the server says they hold. For a client's copy, so it ignores
    /// the accepts check and does not mark anything dirty.
    /// </summary>
    public void Mirror(IEnumerable<SlotChange> changes)
    {
        foreach (var change in changes)
        {
            if (Has(change.Slot))
            {
                _slots[change.Slot] = change.Stack;
            }
        }
    }

    /// <summary>
    /// Puts a saved list of slots back into an empty container, and marks each slot it filled
    /// changed so the owner is sent it. A stack whose item is no longer in the game, that this
    /// slot no longer accepts, or that lies past the last slot is left out, and so is a stack of
    /// a non-stackable item that holds more than one. Returns how many were left out, so the
    /// caller can say so. Throws <see cref="InvalidOperationException"/> when the container is
    /// not empty.
    /// </summary>
    // F5: the count of lost stacks is the caller's cue to log them.
    public int Restore(IReadOnlyList<ItemStack?> saved)
    {
        if (_slots.Any(stack => stack is not null))
        {
            throw new InvalidOperationException("Only an empty container can be restored.");
        }

        var lost = 0;
        for (var slot = 0; slot < saved.Count; slot++)
        {
            if (saved[slot] is not { } stack)
            {
                continue;
            }

            if (Restorable(slot, stack))
            {
                Set(slot, stack);
            }
            else
            {
                lost++;
            }
        }

        return lost;
    }

    private bool Restorable(int slot, ItemStack stack) =>
        _items.Find(stack.Item) is { } def && Has(slot) && (def.Stackable || stack.Count == 1) && Allows(slot, stack.Item);

    private Outcome ExchangeAll(IReadOnlyList<ItemStack> take, IReadOnlyList<ItemStack> give)
    {
        foreach (var stack in take)
        {
            if (!Remove(stack))
            {
                return Outcome.NothingThere;
            }
        }

        foreach (var stack in give)
        {
            if (Add(stack.Item, stack.Count) < stack.Count)
            {
                return Outcome.WontFit;
            }
        }

        return Outcome.Done;
    }

    private bool Remove(ItemStack stack)
    {
        if (CountOf(stack.Item) < stack.Count)
        {
            return false;
        }

        var left = stack.Count;
        for (var slot = 0; slot < Size && left > 0; slot++)
        {
            if (_slots[slot]?.Item == stack.Item)
            {
                left -= Take(slot, left)?.Count ?? 0;
            }
        }

        return true;
    }

    /// <summary>How much of <paramref name="source"/> fits on <paramref name="target"/>: none unless both are one stackable item.</summary>
    private int Movable(ItemStack source, ItemStack target) =>
        source.Item == target.Item && _items.Require(source.Item).Stackable ? Math.Min(ItemStack.MaxCount - target.Count, source.Count) : 0;

    private bool Has(int slot) => slot >= 0 && slot < _slots.Length;

    private bool Allows(int slot, int item) => _accepts is null || _accepts(slot, _items.Require(item));

    private bool AllowsOrEmpty(int slot, ItemStack? stack) => stack is not { } held || Allows(slot, held.Item);

    private void Set(int slot, ItemStack? stack)
    {
        _slots[slot] = stack;
        _dirty.Add(slot);
    }

    /// <summary>The slots of <paramref name="tab"/>, or all of them for null. A tab that does not exist has none.</summary>
    private IEnumerable<int> TabSlots(int? tab)
    {
        if (tab is not { } at)
        {
            return Enumerable.Range(0, Size);
        }

        // Compared as a tab count, so a huge tab from the wire cannot overflow into a real one.
        return at >= 0 && at < Size / TabSize ? Enumerable.Range(at * TabSize, TabSize) : [];
    }

    private long TopUp(int item, long count, IEnumerable<int> slots)
    {
        var left = count;
        foreach (var slot in slots)
        {
            if (left == 0 || _slots[slot] is not { } stack || stack.Item != item)
            {
                continue;
            }

            var put = (int)Math.Min(ItemStack.MaxCount - stack.Count, left);
            if (put == 0)
            {
                continue;
            }

            Set(slot, new ItemStack(item, stack.Count + put));
            left -= put;
        }

        return count - left;
    }

    private long FillEmpty(ItemDef def, long count, IEnumerable<int> slots)
    {
        var left = count;
        foreach (var slot in slots)
        {
            if (left == 0)
            {
                break;
            }

            if (_slots[slot] is not null || !Allows(slot, def.Id))
            {
                continue;
            }

            var put = def.Stackable ? (int)Math.Min(left, ItemStack.MaxCount) : 1;
            Set(slot, new ItemStack(def.Id, put));
            left -= put;
        }

        return count - left;
    }
}
