using System.Buffers.Binary;

namespace Scape.Core;

/// <summary>
/// A region as bytes: the magic, a version, the plane, the region's x and y as two
/// bytes each (little-endian), then one flag byte a tile.
/// </summary>
public static class FlagRegionFile
{
    /// <summary>The version a file is written in. A reader refuses any other.</summary>
    public const int Version = 1;

    private const int HeaderLength = 10;
    private const int FileLength = HeaderLength + TileWorld.RegionSize * TileWorld.RegionSize;

    private static ReadOnlySpan<byte> Magic => "OSFL"u8;

    public static byte[] Encode(FlagRegion region)
    {
        var bytes = new byte[HeaderLength + region.Flags.Length];
        Magic.CopyTo(bytes);
        bytes[4] = Version;
        bytes[5] = (byte)region.Key.Plane;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)region.Key.X);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)region.Key.Y);
        region.Flags.CopyTo(bytes.AsSpan(HeaderLength));
        return bytes;
    }

    /// <summary>
    /// The region in <paramref name="bytes"/>. Throws a <see cref="FormatException"/> for a file
    /// that is not one, is cut short, is in another version or names a region outside the world.
    /// </summary>
    public static FlagRegion Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != FileLength)
        {
            throw new FormatException(FormattableString.Invariant($"A region file is {FileLength} bytes, not {bytes.Length}."));
        }

        if (!bytes[..4].SequenceEqual(Magic))
        {
            throw new FormatException("This is not a region file.");
        }

        if (bytes[4] != Version)
        {
            throw new FormatException(FormattableString.Invariant($"Region file version {bytes[4]} is not read; this reads version {Version}."));
        }

        return new FlagRegion(KeyOf(bytes), bytes[HeaderLength..]);
    }

    private static RegionKey KeyOf(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new RegionKey(
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
                bytes[5]);
        }
        catch (ArgumentException e)
        {
            throw new FormatException("The file names a region outside the world.", e);
        }
    }
}
