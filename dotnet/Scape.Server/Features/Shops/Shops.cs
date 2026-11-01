using Scape.Core;

namespace Scape.Server;

/// <summary>What shops ask of the rest of the server.</summary>
public sealed record ShopsOptions
{
    public required ShopRules Rules { get; init; }

    public required ItemSet Items { get; init; }

    /// <summary>The creatures, whose shops are stocked when the server starts.</summary>
    public required NpcSet Npcs { get; init; }

    /// <summary>Decides when a shop opens, since it is the trade option of a creature.</summary>
    public required Interactions Interactions { get; init; }

    public required Items Bags { get; init; }

    public required Windows Windows { get; init; }
}

/// <summary>
/// Shopkeepers. Trading with one opens its shop. Stock belongs to the kind of creature, so two
/// keepers of one kind share it. A purchase and a sale each change coins, stock and inventory
/// together or not at all.
/// </summary>
public sealed class Shops
{
    private readonly Services _services;
    private readonly ShopsOptions _options;

    // Each shop's lines by creature id, then item, in the order the shop lists them.
    private readonly Dictionary<int, OrderedDictionary<int, Stock>> _stock = [];
    private readonly HashSet<int> _changed = [];

    private Shops(Services services, ShopsOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>Adds shops to <paramref name="registry"/>.</summary>
    /// <exception cref="ArgumentException">
    /// A shop stocks an item that does not exist or the coin itself, or a full stack of a line
    /// would cost more than a stack of coins can hold.
    /// </exception>
    public static void Register(Registry registry, Services services, ShopsOptions options)
    {
        options.Items.Require(options.Rules.Coins);
        var feature = new Shops(services, options);
        foreach (var npc in options.Npcs.Npcs)
        {
            if (npc.Shop is { } shop)
            {
                feature._stock[npc.Id] = feature.StockOf(shop);
            }
        }

        options.Interactions.AddNpcAction(NpcOption.Trade, feature.Open);
        registry.AddIntentHandler(feature.OnIntent);
        registry.AddSystem(Phase.Outbound, feature.Tick);
    }

    private OrderedDictionary<int, Stock> StockOf(ShopDef shop)
    {
        var rules = _options.Rules;
        foreach (var line in shop.Lines)
        {
            var price = rules.BuyPrice(_options.Items.Require(line.Item));
            if (line.Item == rules.Coins || price * line.Cap > ItemStack.MaxCount)
            {
                throw new ArgumentException($"Item {line.Item} cannot be stocked.", nameof(shop));
            }
        }

        return new(shop.Lines.Select(line => KeyValuePair.Create(line.Item, new Stock(line))));
    }

    private void Open(string player, PlacedNpc npc)
    {
        if (!_stock.ContainsKey(npc.Def.Id))
        {
            Tell(player, "They have nothing to sell.");
            return;
        }

        _options.Windows.Open(player, new OpenWindow(WindowKind.Shop, npc.Def.Id, new Beside(Footprint.OfTile(npc.Tile))));
        Show(player, npc.Def.Id);
    }

    private void OnIntent(string player, Intent intent)
    {
        if (_options.Windows.Of(player) is not { Kind: WindowKind.Shop } window)
        {
            return;
        }

        var stock = _stock.GetValueOrDefault(window.Ref);
        switch (intent)
        {
            case Buy buy:
                BuyFrom(player, window.Ref, stock?.GetValueOrDefault(buy.Item), buy.Count);
                break;
            case Sell sell:
                SellTo(player, window.Ref, stock?.GetValueOrDefault(sell.Item), sell.Count);
                break;
        }
    }

    // The wire holds a count between 1 and ItemStack.MaxCount. Startup capped each line's price
    // times its cap at ItemStack.MaxCount, so every count and price below fits an int.
    private void BuyFrom(string player, int shop, Stock? stock, long count)
    {
        if (stock is null || _options.Bags.ContainerOf(player, ContainerId.Inventory) is not { } inventory)
        {
            Tell(player, "They do not sell that.");
            return;
        }

        if (stock.Count < count)
        {
            Tell(player, "They have not got that many.");
            return;
        }

        var item = stock.Line.Item;
        var coins = _options.Rules.Coins;
        var price = _options.Rules.BuyPrice(_options.Items.Require(item)) * count;
        if (inventory.CountOf(coins) < price)
        {
            Tell(player, "You cannot afford that.");
            return;
        }

        if (inventory.Exchange([new ItemStack(coins, (int)price)], [new ItemStack(item, (int)count)]) != Outcome.Done)
        {
            Tell(player, "You have no room for that.");
            return;
        }

        stock.Count -= (int)count;
        _changed.Add(shop);
    }

    private void SellTo(string player, int shop, Stock? stock, long count)
    {
        if (stock is null || _options.Bags.ContainerOf(player, ContainerId.Inventory) is not { } inventory)
        {
            Tell(player, "They do not buy that.");
            return;
        }

        var item = stock.Line.Item;
        var sold = (int)Math.Min(count, Math.Min(stock.Line.Cap - stock.Count, inventory.CountOf(item)));
        if (sold < 1)
        {
            Tell(player, "They will not take any of that.");
            return;
        }

        var pay = _options.Rules.SellPrice(_options.Items.Require(item)) * sold;
        if (inventory.Exchange([new ItemStack(item, sold)], [new ItemStack(_options.Rules.Coins, (int)pay)]) != Outcome.Done)
        {
            Tell(player, "You have no room for the coins.");
            return;
        }

        stock.Count += sold;
        _changed.Add(shop);
    }

    private void Tick()
    {
        foreach (var (shop, lines) in _stock)
        {
            foreach (var stock in lines.Values)
            {
                var before = stock.Count;
                stock.Restock();
                if (stock.Count != before)
                {
                    _changed.Add(shop);
                }
            }
        }

        foreach (var shop in _changed)
        {
            foreach (var player in _options.Windows.Viewers(WindowKind.Shop, shop))
            {
                Show(player, shop);
            }
        }

        _changed.Clear();
    }

    private void Show(string player, int shop) =>
        _services.Hosting.Tell(player, new ShopView(shop, [.. _stock[shop].Values.Select(stock => new ShopStock(stock.Line.Item, stock.Count))]));

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    /// <summary>One line of a shop and how many it holds now.</summary>
    private sealed class Stock(ShopLine line)
    {
        // Ticks since the last item came in, while below the cap.
        private int _waited;

        public ShopLine Line { get; } = line;

        public int Count { get; set; } = line.Start;

        public void Restock()
        {
            if (Count >= Line.Cap)
            {
                _waited = 0;
                return;
            }

            _waited++;
            if (_waited < Line.RestockTicks)
            {
                return;
            }

            Count++;
            _waited = 0;
        }
    }
}
