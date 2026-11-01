using Scape.Core;

namespace Scape.Content;

/// <summary>
/// Coins are the money. A shop sells for 130 percent of an item's value and
/// pays 40 percent.
/// </summary>
public static class DefaultShops
{
    public static ShopRules Rules { get; } = new(ItemId.Coins);
}
