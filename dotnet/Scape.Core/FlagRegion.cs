namespace Scape.Core;

/// <summary>
/// The flags of one region: <see cref="TileWorld.RegionSize"/> squared tiles, row by row
/// from the south-west corner, one byte each.
/// </summary>
public sealed class FlagRegion
{
    private readonly byte[] _flags;

    /// <summary>A region over a copy of <paramref name="flags"/>, which must hold one byte a tile.</summary>
    public FlagRegion(RegionKey key, ReadOnlySpan<byte> flags)
    {
        if (flags.Length != TileWorld.RegionSize * TileWorld.RegionSize)
        {
            throw new ArgumentException("One byte a tile.", nameof(flags));
        }

        Key = key;
        _flags = flags.ToArray();
    }

    public RegionKey Key { get; }

    public ReadOnlySpan<byte> Flags => _flags;

    /// <summary>A region with nothing on it.</summary>
    public static FlagRegion Open(RegionKey key) => new(key, new byte[TileWorld.RegionSize * TileWorld.RegionSize]);

    public TileFlags FlagsAtLocal(int index) => new(_flags[index]);

    public void SetLocal(int index, TileFlags value) => _flags[index] = (byte)value.Bits;
}
