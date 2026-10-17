import 'dart:math';

import 'items.dart';

/// Slots in an inventory.
const int inventorySize = 28;

/// Tabs in a bank, and the slots each holds.
const int bankTabs = 8;
const int bankTabSize = 100;

/// The containers a player owns. The wire names one by its index.
enum ContainerId { inventory, equipment, bank }

/// How many slots the container for [id] has.
int containerSize(ContainerId id) => switch (id) {
  ContainerId.inventory => inventorySize,
  ContainerId.equipment => equipmentSlots.length,
  ContainerId.bank => bankTabs * bankTabSize,
};

/// How an operation ended. A refusal is a value, never an exception, because
/// a full bag is a normal thing for a player to meet.
enum Outcome { done, partly, nothingThere, noSuchSlot, wontFit, notAllowed }

/// Whether [item] may sit in [slot].
typedef Accepts = bool Function(int slot, ItemDef item);

/// What a slot holds now. A null stack is an empty slot.
typedef SlotChange = ({int slot, ItemStack? stack});

/// A fixed row of slots, each empty or holding a stack.
///
/// Each operation is atomic: it moves everything it reports or nothing. The
/// slots an operation changed are listed in [dirty] until [markClean], so the
/// server can send a change once a tick.
// F5: each command returns its outcome, since a refusal is not an error.
final class Container {
  Container({
    required ItemSet items,
    required int size,
    Accepts? accepts,
    int? tabSize,
  }) : _items = items,
       _accepts = accepts,
       tabSize = tabSize ?? size,
       _slots = List.filled(size, null) {
    if (size < 1) throw ArgumentError.value(size, 'size', 'is below one');
    if (this.tabSize < 1 || size % this.tabSize != 0) {
      throw ArgumentError.value(tabSize, 'tabSize', 'does not divide the size');
    }
  }

  /// The container a player owns for [id].
  factory Container.forId(ContainerId id, ItemSet items) => switch (id) {
    ContainerId.inventory => Container(items: items, size: inventorySize),
    ContainerId.equipment => Container(
      items: items,
      size: containerSize(id),
      accepts: (slot, item) => item.equipSlot == equipmentSlots[slot],
    ),
    ContainerId.bank => Container(
      items: items,
      size: containerSize(id),
      tabSize: bankTabSize,
    ),
  };

  final ItemSet _items;
  final Accepts? _accepts;
  final List<ItemStack?> _slots;
  final Set<int> _dirty = {};

  /// Slots in each tab. A container without tabs is one tab.
  final int tabSize;

  int get size => _slots.length;

  /// The stack in each slot, an empty slot as null.
  List<ItemStack?> get slots => List.unmodifiable(_slots);

  /// The slots changed since the last [markClean], lowest first.
  List<int> get dirty => _dirty.toList()..sort();

  void markClean() => _dirty.clear();

  /// The stack in [slot], or null when it is empty or there is no such slot.
  ItemStack? at(int slot) => _has(slot) ? _slots[slot] : null;

  /// The total of [item] over every slot.
  int countOf(int item) {
    var total = 0;
    for (final stack in _slots) {
      if (stack != null && stack.item == item) total += stack.count;
    }
    return total;
  }

  /// The slots that hold something, in [tab] or everywhere when it is null.
  List<int> occupied({int? tab}) => [
    for (final slot in _tabSlots(tab))
      if (_slots[slot] != null) slot,
  ];

  /// The occupied slots whose item name holds [text], whatever the case.
  List<int> search(String text, {int? tab}) {
    final needle = text.toLowerCase();
    return [
      for (final slot in occupied(tab: tab))
        if (_nameAt(slot).contains(needle)) slot,
    ];
  }

  /// Whether one more of [item] would go in. Throws [ArgumentError] for an
  /// item that does not exist.
  bool canHold(int item) {
    final def = _items.require(item);
    return _slots.indexed.any((entry) {
      final (slot, stack) = entry;
      if (stack == null) return _allows(slot, ItemStack(item, 1));
      return def.stackable && stack.item == item && stack.count < maxStack;
    });
  }

  /// Puts up to [count] of [item] in, into [tab] or anywhere when it is null,
  /// and returns how many went in. A stackable item fills its own stacks
  /// first. Throws [ArgumentError] for an item that does not exist and
  /// [RangeError] for a count below one.
  int add(int item, int count, {int? tab}) {
    final def = _items.require(item);
    if (count < 1) throw RangeError.value(count, 'count', 'is below one');
    final slots = _tabSlots(tab);
    var left = count;
    if (def.stackable) left -= _topUp(item, left, slots);
    left -= _fillEmpty(def, left, slots);
    return count - left;
  }

  /// Takes up to [count] out of [slot], or all of it, and returns what came
  /// out. Null when there is nothing to take.
  ItemStack? take(int slot, [int? count]) {
    final stack = at(slot);
    if (stack == null) return null;
    final taken = min(count ?? stack.count, stack.count);
    if (taken < 1) return null;
    _set(slot, taken == stack.count ? null : _with(stack, stack.count - taken));
    return ItemStack(stack.item, taken);
  }

  /// Moves the stack in [from] to the empty slot [to].
  Outcome move(int from, int to) {
    if (!_has(from) || !_has(to)) return Outcome.noSuchSlot;
    final stack = _slots[from];
    if (stack == null) return Outcome.nothingThere;
    if (_slots[to] != null) return Outcome.wontFit;
    if (!_allows(to, stack)) return Outcome.notAllowed;
    _set(to, stack);
    _set(from, null);
    return Outcome.done;
  }

  /// Exchanges two slots. One of them may be empty.
  Outcome swap(int a, int b) {
    if (!_has(a) || !_has(b)) return Outcome.noSuchSlot;
    final first = _slots[a];
    final second = _slots[b];
    if (first == null && second == null) return Outcome.nothingThere;
    if (a == b) return Outcome.done;
    if (!_allowsOrEmpty(b, first) || !_allowsOrEmpty(a, second)) {
      return Outcome.notAllowed;
    }
    _set(a, second);
    _set(b, first);
    return Outcome.done;
  }

  /// Moves as much of the stack in [from] onto the same item in [to] as
  /// fits under [maxStack].
  Outcome stack(int from, int to) {
    if (!_has(from) || !_has(to)) return Outcome.noSuchSlot;
    final source = _slots[from];
    final target = _slots[to];
    if (source == null || target == null) return Outcome.nothingThere;
    final shared =
        from != to &&
        source.item == target.item &&
        _items.require(source.item).stackable;
    final moved = shared ? min(maxStack - target.count, source.count) : 0;
    if (moved == 0) return Outcome.wontFit;
    _set(to, _with(target, target.count + moved));
    _set(
      from,
      moved == source.count ? null : _with(source, source.count - moved),
    );
    return moved == source.count ? Outcome.done : Outcome.partly;
  }

  /// Moves [count] from the stack in [from] to the empty slot [to], leaving
  /// the rest. [count] must be below the stack, or the whole move is [move].
  Outcome split(int from, int to, int count) {
    if (!_has(from) || !_has(to)) return Outcome.noSuchSlot;
    final stack = _slots[from];
    if (stack == null) return Outcome.nothingThere;
    if (count < 1 || count >= stack.count || _slots[to] != null) {
      return Outcome.wontFit;
    }
    final piece = _with(stack, count);
    if (!_allows(to, piece)) return Outcome.notAllowed;
    _set(to, piece);
    _set(from, _with(stack, stack.count - count));
    return Outcome.done;
  }

  /// Moves up to [count] of the stack in [slot] into [into], at [tab] or
  /// anywhere. A deposit and a withdrawal are this call with the containers
  /// the other way round. What does not fit stays in [slot].
  Outcome transfer(int slot, Container into, {int? count, int? tab}) {
    if (identical(this, into)) return Outcome.notAllowed;
    if (!_has(slot)) return Outcome.noSuchSlot;
    final stack = _slots[slot];
    if (stack == null) return Outcome.nothingThere;
    final want = min(count ?? stack.count, stack.count);
    if (want < 1) return Outcome.wontFit;
    final added = into.add(stack.item, want, tab: tab);
    if (added == 0) return Outcome.wontFit;
    take(slot, added);
    return added == want ? Outcome.done : Outcome.partly;
  }

  /// Takes [take] out and puts [give] in as one change. When [take] is not all
  /// held, or [give] does not all fit, nothing changes and no slot is marked
  /// dirty. What [take] frees is there for [give] to use. Throws
  /// [ArgumentError] for an item that does not exist.
  // F5: each command returns its outcome, since a refusal is not an error.
  Outcome exchange({
    List<ItemStack> take = const [],
    List<ItemStack> give = const [],
  }) {
    for (final stack in [...take, ...give]) {
      _items.require(stack.item);
    }
    final before = List<ItemStack?>.of(_slots);
    final dirtyBefore = Set<int>.of(_dirty);
    final outcome = _exchange(take, give);
    if (outcome != Outcome.done) {
      _slots.setAll(0, before);
      _dirty
        ..clear()
        ..addAll(dirtyBefore);
    }
    return outcome;
  }

  /// Whether [exchange] with the same arguments would be done. Nothing
  /// changes, and nothing is marked dirty.
  bool canExchange({
    List<ItemStack> take = const [],
    List<ItemStack> give = const [],
  }) {
    final probe = Container(
      items: _items,
      size: size,
      accepts: _accepts,
      tabSize: tabSize,
    )..restore(_slots);
    return probe.exchange(take: take, give: give) == Outcome.done;
  }

  /// Sets slots to what the server says they hold. For a client's copy, so
  /// it ignores [accepts] and does not mark anything dirty.
  void mirror(List<SlotChange> changes) {
    for (final (:slot, :stack) in changes) {
      if (_has(slot)) _slots[slot] = stack;
    }
  }

  /// Puts a saved list of slots back into an empty container, and marks each
  /// slot it filled changed so the owner is sent it. A stack whose item is no
  /// longer in the game, that this slot no longer accepts, or that lies past
  /// the last slot is left out, and so is a stack of a non-stackable item that
  /// holds more than one. Returns how many were left out, so the caller can
  /// say so. Throws [StateError] when the container is not empty.
  // F5: the count of lost stacks is the caller's cue to log them.
  int restore(List<ItemStack?> saved) {
    if (_slots.any((stack) => stack != null)) {
      throw StateError('Only an empty container can be restored.');
    }
    var lost = 0;
    for (final (slot, stack) in saved.indexed) {
      if (stack == null) continue;
      final def = _items.find(stack.item);
      if (def != null &&
          _has(slot) &&
          (def.stackable || stack.count == 1) &&
          _allows(slot, stack)) {
        _set(slot, stack);
      } else {
        lost++;
      }
    }
    return lost;
  }

  /// The lower case name of the item in [slot], which must be occupied.
  String _nameAt(int slot) =>
      _items.require(_slots[slot]!.item).name.toLowerCase();

  Outcome _exchange(List<ItemStack> take, List<ItemStack> give) {
    for (final stack in take) {
      if (!_remove(stack)) return Outcome.nothingThere;
    }
    for (final stack in give) {
      if (add(stack.item, stack.count) < stack.count) return Outcome.wontFit;
    }
    return Outcome.done;
  }

  bool _remove(ItemStack stack) {
    if (countOf(stack.item) < stack.count) return false;
    var left = stack.count;
    for (var slot = 0; slot < size && left > 0; slot++) {
      if (_slots[slot]?.item == stack.item) {
        left -= take(slot, left)?.count ?? 0;
      }
    }
    return true;
  }

  bool _has(int slot) => slot >= 0 && slot < _slots.length;

  bool _allows(int slot, ItemStack stack) =>
      _accepts == null || _accepts(slot, _items.require(stack.item));

  bool _allowsOrEmpty(int slot, ItemStack? stack) =>
      stack == null || _allows(slot, stack);

  ItemStack _with(ItemStack stack, int count) => ItemStack(stack.item, count);

  void _set(int slot, ItemStack? stack) {
    _slots[slot] = stack;
    _dirty.add(slot);
  }

  /// The slots of [tab], or all of them for null. A tab that does not exist
  /// has none.
  Iterable<int> _tabSlots(int? tab) {
    if (tab == null) return Iterable<int>.generate(size);
    if (tab < 0 || tab * tabSize >= size) return const [];
    return Iterable<int>.generate(tabSize, (i) => tab * tabSize + i);
  }

  int _topUp(int item, int count, Iterable<int> slots) {
    var left = count;
    for (final slot in slots) {
      final stack = _slots[slot];
      if (left == 0 || stack == null || stack.item != item) continue;
      final put = min(maxStack - stack.count, left);
      if (put == 0) continue;
      _set(slot, _with(stack, stack.count + put));
      left -= put;
    }
    return count - left;
  }

  int _fillEmpty(ItemDef def, int count, Iterable<int> slots) {
    var left = count;
    for (final slot in slots) {
      if (left == 0) break;
      if (_slots[slot] != null) continue;
      final put = def.stackable ? min(left, maxStack) : 1;
      final stack = ItemStack(def.id, put);
      if (!_allows(slot, stack)) continue;
      _set(slot, stack);
      left -= put;
    }
    return count - left;
  }
}
