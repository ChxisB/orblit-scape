using System.Buffers.Binary;

namespace Scape.Core;

/// <summary>
/// Where an actor is drawn, in tiles. Between two ticks it is a blend of two tiles,
/// so the numbers are not whole.
/// </summary>
public readonly record struct TilePoint(double X, double Y, int Plane);

/// <summary>The replicated component holding where an actor stands.</summary>
public static class TilePosition
{
    public const string Name = "TilePosition";

    /// <summary>
    /// Floats in the component: x, the plane, then y. That puts a tile where an interest
    /// grid reads a position, as x, up, z.
    /// </summary>
    public const int Floats = 3;

    public static void Write(Span<byte> bytes, TileCoord tile)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, tile.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], tile.Plane);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], tile.Y);
    }

    /// <summary>Reads the component's bytes as they are, with no rounding.</summary>
    public static TilePoint Read(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]),
        Round(BinaryPrimitives.ReadSingleLittleEndian(bytes[4..])));

    /// <summary>The tile a point is nearest to.</summary>
    public static TileCoord Nearest(TilePoint point) => new(Round(point.X), Round(point.Y), point.Plane);

    // Halves round away from zero, and a value with no int, such as NaN, throws.
    private static int Round(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
}
