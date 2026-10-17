import 'dart:typed_data';

import 'container.dart';
import 'items.dart';
import 'messages.dart';

/// Bytes in a delta before its entries: the tag, then the container.
const int _head = 2;

/// Bytes in one entry: the slot, the item and the count.
const int _entryBytes = 8;

const int _entriesPerDelta = (maxDeltaBytes - _head) ~/ _entryBytes;

/// What a decoded delta says about one container.
typedef SlotsDelta = ({ContainerId container, List<SlotChange> changes});

/// Writes what [slots] of [container] hold now, as one or more
/// [PrivateState] deltas. A bank is too large for one, so a long list is
/// split. Each delta is whole on its own.
List<Uint8List> encodeSlots(
  ContainerId id,
  Container container,
  Iterable<int> slots,
) {
  final all = slots.toList();
  return [
    for (var start = 0; start < all.length; start += _entriesPerDelta)
      _encodeChunk(
        id,
        container,
        all.sublist(
          start,
          start + _entriesPerDelta > all.length
              ? all.length
              : start + _entriesPerDelta,
        ),
      ),
  ];
}

Uint8List _encodeChunk(ContainerId id, Container container, List<int> slots) {
  final bytes = Uint8List(_head + slots.length * _entryBytes);
  bytes[0] = itemsDeltaTag;
  bytes[1] = id.index;
  final data = ByteData.sublistView(bytes);
  for (var i = 0; i < slots.length; i++) {
    final at = _head + i * _entryBytes;
    final stack = container.at(slots[i]);
    data
      ..setUint16(at, slots[i], Endian.little)
      ..setUint16(at + 2, stack?.item ?? 0, Endian.little)
      ..setUint32(at + 4, stack?.count ?? 0, Endian.little);
  }
  return bytes;
}

/// Reads a delta written by [encodeSlots], or null when it is not one: the
/// wrong tag, a length that is not whole entries, a container or an item
/// that does not exist, or a count the item cannot have.
SlotsDelta? tryDecodeSlots(Uint8List delta, ItemSet items) {
  if (delta.length < _head || delta[0] != itemsDeltaTag) return null;
  if ((delta.length - _head) % _entryBytes != 0) return null;
  if (delta[1] >= ContainerId.values.length) return null;
  final data = ByteData.sublistView(delta);
  final changes = <SlotChange>[];
  for (var at = _head; at < delta.length; at += _entryBytes) {
    final change = _readEntry(data, at, items);
    if (change == null) return null;
    changes.add(change);
  }
  return (container: ContainerId.values[delta[1]], changes: changes);
}

SlotChange? _readEntry(ByteData data, int at, ItemSet items) {
  final slot = data.getUint16(at, Endian.little);
  final item = data.getUint16(at + 2, Endian.little);
  final count = data.getUint32(at + 4, Endian.little);
  if (item == 0) return count == 0 ? (slot: slot, stack: null) : null;
  final def = items.find(item);
  if (def == null || count < 1 || count > maxStack) return null;
  if (!def.stackable && count != 1) return null;
  return (slot: slot, stack: ItemStack(item, count));
}
