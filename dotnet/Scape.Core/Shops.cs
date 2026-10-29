namespace Scape.Core;

/// <summary>
/// How a game prices things. A shop sells an item for its value times
/// <see cref="BuyPercent"/> and pays for one at its value times <see cref="SellPercent"/>,
/// and a price is never below one coin. Buying back never pays more than the sale.
/// </summary>
public sealed class ShopRules
{
    public ShopRules(int coins, int buyPercent = 130, int sellPercent = 40)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(coins, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(coins, ItemDef.MaxId);
        if (sellPercent < 1 || buyPercent < sellPercent)
        {
            throw new ArgumentException("A shop sells for at least what it pays.");
        }

        Coins = coins;
        BuyPercent = buyPercent;
        SellPercent = sellPercent;
    }

    /// <summary>The item a price is paid in.</summary>
    public int Coins { get; }

    public int BuyPercent { get; }

    public int SellPercent { get; }

    /// <summary>What a player pays for one <paramref name="item"/>.</summary>
    public int BuyPrice(ItemDef item) => Math.Max(1, item.Value * BuyPercent / 100);

    /// <summary>What a player is paid for one <paramref name="item"/>.</summary>
    public int SellPrice(ItemDef item) => Math.Max(1, item.Value * SellPercent / 100);
}

/// <summary>
/// One item a shop holds: it starts at <see cref="Start"/>, which is <see cref="Cap"/> unless
/// given, and gains one every <see cref="RestockTicks"/> ticks while below the cap. It buys
/// that item only up to the cap, so a line that starts low is one players supply.
/// </summary>
public sealed class ShopLine
{
    /// <summary>The most one line may hold.</summary>
    public const int MaxStock = 1000000;

    public ShopLine(int item, int cap, int? start = null, int restockTicks = 50)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(item, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(item, ItemDef.MaxId);
        ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cap, MaxStock);
        Start = start ?? cap;
        if (Start < 0 || Start > cap)
        {
            throw new ArgumentException(FormattableString.Invariant($"The start {Start} is outside 0 to {cap}."), nameof(start));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(restockTicks, 1);
        Item = item;
        Cap = cap;
        RestockTicks = restockTicks;
    }

    public int Item { get; }

    public int Cap { get; }

    public int Start { get; }

    public int RestockTicks { get; }
}

/// <summary>What a shopkeeper stocks.</summary>
public sealed class ShopDef
{
    /// <summary>The most lines one shop may stock. A shop's whole stock is sent in one message.</summary>
    public const int MaxLines = 40;

    public ShopDef(IReadOnlyList<ShopLine> lines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lines.Count, 1, nameof(lines));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lines.Count, MaxLines, nameof(lines));
        if (lines.DistinctBy(l => l.Item).Count() != lines.Count)
        {
            throw new ArgumentException("The lines repeat an item.", nameof(lines));
        }

        Lines = [.. lines];
    }

    public IReadOnlyList<ShopLine> Lines { get; }

    public ShopLine? Find(int item) => Lines.FirstOrDefault(l => l.Item == item);
}
