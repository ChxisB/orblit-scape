namespace Scape.Core;

/// <summary>What stands on a tile, as bits. A wall flag is a wall along that edge of the tile.</summary>
public readonly record struct TileFlags(int Bits)
{
    public static readonly TileFlags Open = new(0);
    public static readonly TileFlags Blocked = new(1);
    public static readonly TileFlags WallNorth = new(2);
    public static readonly TileFlags WallEast = new(4);
    public static readonly TileFlags WallSouth = new(8);
    public static readonly TileFlags WallWest = new(16);
    public static readonly TileFlags Water = new(32);

    /// <summary>Tiles that people may enter and creatures may not.</summary>
    public static readonly TileFlags NoNpc = new(64);

    public static TileFlags operator |(TileFlags left, TileFlags right) => new(left.Bits | right.Bits);

    /// <summary>Whether any bit of <paramref name="flag"/> is set.</summary>
    public bool Has(TileFlags flag) => (Bits & flag.Bits) != 0;

    /// <summary>These flags with every bit of <paramref name="flag"/> cleared.</summary>
    public TileFlags Without(TileFlags flag) => new(Bits & ~flag.Bits);
}
