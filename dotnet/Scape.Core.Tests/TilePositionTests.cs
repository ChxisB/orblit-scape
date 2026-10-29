using System.Buffers.Binary;

namespace Scape.Core.Tests;

public class TilePositionTests
{
    [Fact]
    public void ATileRoundTripsThroughTheBytes()
    {
        var bytes = new byte[TilePosition.Floats * 4];
        TilePosition.Write(bytes, new TileCoord(16383, 12, 3));

        Assert.Equal(new TilePoint(16383, 12, 3), TilePosition.Read(bytes));
        Assert.Equal(new TileCoord(16383, 12, 3), TilePosition.Nearest(TilePosition.Read(bytes)));
    }

    [Fact]
    public void PutsXFirstAndYLastSoAGridReadsXAndZ()
    {
        var bytes = new byte[12];
        TilePosition.Write(bytes, new TileCoord(5, 9, 2));

        Assert.Equal(5, BinaryPrimitives.ReadSingleLittleEndian(bytes));
        Assert.Equal(2, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4)));
        Assert.Equal(9, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(8)));
    }

    [Fact]
    public void APointBetweenTwoTilesRoundsToTheNearer() =>
        Assert.Equal(new TileCoord(3, 8, 0), TilePosition.Nearest(new TilePoint(3.4, 7.6, 0)));

    [Fact]
    public void APointThatIsNotANumberHasNoNearestTile() =>
        Assert.Throws<OverflowException>(() => TilePosition.Nearest(new TilePoint(double.NaN, 0, 0)));
}
