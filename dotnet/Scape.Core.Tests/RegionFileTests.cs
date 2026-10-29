namespace Scape.Core.Tests;

public class RegionFileTests
{
    private static byte[] OpenFile() => FlagRegionFile.Encode(FlagRegion.Open(new RegionKey(0, 0, 0)));

    [Fact]
    public void ARegionRoundTripsThroughTheFile()
    {
        var region = FlagRegion.Open(new RegionKey(3, 5, 2));
        region.SetLocal(0, TileFlags.Blocked);
        region.SetLocal(65, TileFlags.WallNorth | TileFlags.Water);
        region.SetLocal(4095, TileFlags.NoNpc);

        var back = FlagRegionFile.Decode(FlagRegionFile.Encode(region));

        Assert.Equal(region.Key, back.Key);
        Assert.Equal(region.Flags.ToArray(), back.Flags.ToArray());
    }

    [Fact]
    public void AnUnknownVersionIsRefused()
    {
        var bytes = OpenFile();
        bytes[4] = FlagRegionFile.Version + 1;
        var error = Assert.Throws<FormatException>(() => FlagRegionFile.Decode(bytes));
        Assert.Contains("version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithTheWrongMagicIsRefused()
    {
        var bytes = OpenFile();
        bytes[0] = 0;
        Assert.Throws<FormatException>(() => FlagRegionFile.Decode(bytes));
    }

    [Fact]
    public void AFileOfTheWrongLengthIsRefused()
    {
        var bytes = OpenFile();
        Assert.Throws<FormatException>(() => FlagRegionFile.Decode(bytes.AsSpan(0, bytes.Length - 1)));
        Assert.Throws<FormatException>(() => FlagRegionFile.Decode(new byte[3]));
    }

    [Fact]
    public void AKeyOutsideTheWorldIsRefused()
    {
        var bytes = OpenFile();
        bytes[5] = TileWorld.PlaneCount;
        Assert.Throws<FormatException>(() => FlagRegionFile.Decode(bytes));
    }
}
