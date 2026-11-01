using System.Buffers.Binary;

namespace Scape.Core;

/// <summary>What a decoded delta says about one container.</summary>
public readonly record struct SlotsDelta(ContainerId Container, IReadOnlyList<SlotChange> Changes);

/// <summary>Writes the slots of a container as <see cref="PrivateState"/> deltas, and reads them back.</summary>
public static class ContainerCodec
{
    // Bytes in a delta before its entries: the tag, then the container.
    private const int Head = 2;

    // Bytes in one entry: the slot, the item and the count.
    private const int EntryBytes = 8;

    private const int EntriesPerDelta = (Wire.MaxDeltaBytes - Head) / EntryBytes;

    /// <summary>
    /// Writes what <paramref name="slots"/> of <paramref name="container"/> hold now, as one or
    /// more <see cref="PrivateState"/> deltas. A bank is too large for one, so a long list is
    /// split. Each delta is whole on its own.
    /// </summary>
    public static IReadOnlyList<byte[]> EncodeSlots(ContainerId id, Container container, IEnumerable<int> slots) =>
        [.. slots.Chunk(EntriesPerDelta).Select(chunk => EncodeChunk(id, container, chunk))];

    /// <summary>
    /// Reads a delta written by <see cref="EncodeSlots"/>, or null when it is not one: the wrong
    /// tag, a length that is not whole entries, a container or an item that does not exist, or a
    /// count the item cannot have.
    /// </summary>
    public static SlotsDelta? TryDecodeSlots(ReadOnlySpan<byte> delta, ItemSet items)
    {
        if (delta.Length < Head || delta[0] != ItemSet.DeltaTag || (delta.Length - Head) % EntryBytes != 0 || !Enum.IsDefined((ContainerId)delta[1]))
        {
            return null;
        }

        var changes = new List<SlotChange>();
        for (var at = Head; at < delta.Length; at += EntryBytes)
        {
            if (ReadEntry(delta[at..], items) is not { } change)
            {
                return null;
            }

            changes.Add(change);
        }

        return new SlotsDelta((ContainerId)delta[1], changes.AsReadOnly());
    }

    private static byte[] EncodeChunk(ContainerId id, Container container, int[] slots)
    {
        var bytes = new byte[Head + (slots.Length * EntryBytes)];
        bytes[0] = ItemSet.DeltaTag;
        bytes[1] = (byte)id;
        for (var i = 0; i < slots.Length; i++)
        {
            var entry = bytes.AsSpan(Head + (i * EntryBytes));
            var stack = container.At(slots[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)slots[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], (ushort)(stack?.Item ?? 0));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)(stack?.Count ?? 0));
        }

        return bytes;
    }

    private static SlotChange? ReadEntry(ReadOnlySpan<byte> entry, ItemSet items)
    {
        var slot = BinaryPrimitives.ReadUInt16LittleEndian(entry);
        var item = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
        if (item == 0)
        {
            return count == 0 ? new SlotChange(slot, null) : null;
        }

        if (items.Find(item) is not { } def || count < 1 || count > ItemStack.MaxCount || (!def.Stackable && count != 1))
        {
            return null;
        }

        return new SlotChange(slot, new ItemStack(item, (int)count));
    }
}
