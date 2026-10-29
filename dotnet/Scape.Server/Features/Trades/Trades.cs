using Scape.Core;

namespace Scape.Server;

/// <summary>What trades ask of the rest of the server.</summary>
public sealed record TradesOptions
{
    /// <summary>Decides when a request is made, since it is the click on another player.</summary>
    public required Interactions Interactions { get; init; }

    public required Movement Movement { get; init; }

    /// <summary>Hides the request of a player the other ignores.</summary>
    public required Social Social { get; init; }

    public required Items Bags { get; init; }

    /// <summary>Names the items in the log line of a trade.</summary>
    public required ItemSet Items { get; init; }

    public required Windows Windows { get; init; }

    /// <summary>Takes a line for each trade made, so an operator can follow items between players.</summary>
    public required Action<string> Log { get; init; }
}

/// <summary>
/// Two players swap items. One clicks the other, the other clicks back, and a window opens for
/// both. Each puts up what they like, both accept, both accept again at the confirm step, and the
/// swap is one change. A trade ends when either player leaves, opens another window or walks off
/// the tile they stood on.
/// </summary>
public sealed class Trades
{
    /// <summary>Ticks a request to trade waits for the other player, one minute.</summary>
    private const int RequestTicks = 100;

    /// <summary>Tiles, each way, two players may stand apart when a trade opens.</summary>
    private const int OpenRange = 2;

    private const string Cancelled = "The trade was cancelled.";

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly TradesOptions _options;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, (string To, long Until)> _requests = [];

    private Trades(Registry registry, Services services, TradesOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
    }

    /// <summary>Adds trading to <paramref name="registry"/>. A click on a player asks to trade.</summary>
    public static void Register(Registry registry, Services services, TradesOptions options)
    {
        var feature = new Trades(registry, services, options);
        options.Interactions.SetPlayerAction(feature.Request);
        registry.AddIntentHandler(feature.OnIntent);
        registry.AddGoneHandler(feature.Gone);
        registry.AddSystem(Phase.Outbound, feature.EndClosed);
    }

    private static bool IsNear(TileCoord a, TileCoord b) =>
        a.Plane == b.Plane && Math.Abs(a.X - b.X) <= OpenRange && Math.Abs(a.Y - b.Y) <= OpenRange;

    /// <summary>What stops <paramref name="bag"/> from giving <paramref name="gives"/> and taking <paramref name="gets"/>, or null.</summary>
    private static string? Blocker(Container bag, List<ItemStack> gives, List<ItemStack> gets) => bag.TryExchange(gives, gets) switch
    {
        Outcome.Done => null,
        Outcome.WontFit => "room for that",
        _ => "everything offered",
    };

    private void Request(string player, string other)
    {
        if (IsBusy(player) || IsBusy(other))
        {
            Tell(player, IsBusy(player) ? "You are busy." : "That player is busy.");
        }
        else if (HasAsked(other, player) && !_options.Social.IsIgnoring(other, player))
        {
            _requests.Remove(other);
            _requests.Remove(player);
            Open(player, other);
        }
        else
        {
            Ask(player, other);
        }
    }

    private void Ask(string player, string other)
    {
        Tell(player, $"Sending a trade request to {other}.");

        // An ignored sender is told what any sender is told, so ignoring does not give itself away.
        if (_options.Social.IsIgnoring(other, player) || HasAsked(player, other))
        {
            return;
        }

        _requests[player] = (other, _registry.Actions.Tick + RequestTicks);
        Tell(other, $"{player} wants to trade with you.");
    }

    private bool HasAsked(string player, string other) =>
        _requests.TryGetValue(player, out var request) && request.To == other;

    private bool IsBusy(string player) => _options.Windows.Of(player) is not null || _sessions.ContainsKey(player);

    private void Open(string player, string other)
    {
        if (_options.Movement.TileOf(player) is not { } here
            || _options.Movement.TileOf(other) is not { } there
            || !IsNear(here, there))
        {
            Tell(player, "That player is too far away.");
            return;
        }

        // Sorted, so the log names the pair the same way whoever clicked last.
        var session = string.CompareOrdinal(player, other) < 0 ? new Session(player, other) : new Session(other, player);
        _sessions[player] = _sessions[other] = session;
        _options.Windows.Open(player, new OpenWindow(WindowKind.Trade, 0, new OnTile(here)));
        _options.Windows.Open(other, new OpenWindow(WindowKind.Trade, 0, new OnTile(there)));
        Show(session);
    }

    private void OnIntent(string player, Intent intent)
    {
        if (!_sessions.TryGetValue(player, out var session))
        {
            return;
        }

        switch (intent)
        {
            case TradeOffer offer:
                Offer(session, player, offer);
                break;
            case TradeAccept:
                Accept(session, player);
                break;
            case TradeDecline:
                End(session, $"{player} declined the trade.");
                break;
        }
    }

    private void Offer(Session session, string player, TradeOffer offer)
    {
        var offered = session.Offered(player, offer.Item);
        if (offer.Count > (Inventory(player)?.CountOf(offer.Item) ?? 0))
        {
            Tell(player, "You do not have that many.");
            return;
        }

        // A repeat of the offer would only set both accepts back.
        if (offer.Count == offered)
        {
            return;
        }

        if (offered == 0 && session.LinesOf(player) >= Wire.MaxTradeLines)
        {
            Tell(player, "You cannot offer that many different items.");
            return;
        }

        // The wire caps a count at ItemStack.MaxCount, so it fits an int.
        session.Offer(player, offer.Item, (int)offer.Count);
        Show(session);
    }

    private void Accept(Session session, string player)
    {
        if (session.Accept(player))
        {
            Swap(session);
        }
        else
        {
            Show(session);
        }
    }

    /// <summary>Makes the swap, or says why it cannot be made and goes back to the offer step.</summary>
    private void Swap(Session session)
    {
        if (Inventory(session.A) is not { } bagA || Inventory(session.B) is not { } bagB)
        {
            End(session, Cancelled);
            return;
        }

        var givesA = session.StacksOf(session.A);
        var givesB = session.StacksOf(session.B);
        var blockA = Blocker(bagA, givesA, givesB);
        var blockB = Blocker(bagB, givesB, givesA);
        if (blockA is not null || blockB is not null)
        {
            Refuse(session, blockA, blockB);
            return;
        }

        if (givesA.Count == 0 && givesB.Count == 0)
        {
            End(session, "Nothing was traded.");
            return;
        }

        bagA.Exchange(givesA, givesB);
        bagB.Exchange(givesB, givesA);
        _options.Log($"Trade: {session.A} gave {Describe(givesA)}, {session.B} gave {Describe(givesB)}");
        End(session, "The trade is complete.");
    }

    private void Refuse(Session session, string? blockA, string? blockB)
    {
        session.Reset();
        TellBlocked(session.A, session.B, blockA);
        TellBlocked(session.B, session.A, blockB);
        Show(session);
    }

    private void TellBlocked(string player, string other, string? reason)
    {
        if (reason is null)
        {
            return;
        }

        Tell(player, $"The trade cannot go through. You do not have {reason}.");
        Tell(other, $"The trade cannot go through. {player} does not have {reason}.");
    }

    private string Describe(List<ItemStack> stacks) => stacks.Count == 0
        ? "nothing"
        : string.Join(", ", stacks.Select(stack => $"{stack.Count} x {_options.Items.Require(stack.Item).Name}"));

    private void Show(Session session)
    {
        foreach (var player in session.Players)
        {
            var other = session.Other(player);
            var step = new TradeStep(session.Confirming, session.HasAccepted(player), session.HasAccepted(other));
            _services.Hosting.Tell(player, new TradeView(other, session.StacksOf(player), session.StacksOf(other), step));
        }
    }

    private void End(Session session, string why)
    {
        foreach (var player in session.Players)
        {
            _sessions.Remove(player);
            if (_options.Windows.Of(player)?.Kind == WindowKind.Trade)
            {
                _options.Windows.Close(player);
            }

            Tell(player, why);
        }
    }

    private void Gone(string player)
    {
        _requests.Remove(player);
        if (_sessions.TryGetValue(player, out var session))
        {
            End(session, $"{player} left the trade.");
        }
    }

    /// <summary>
    /// Ends the trade of a player whose window closed, by walking off or by opening another, and
    /// drops requests that nobody answered.
    /// </summary>
    private void EndClosed()
    {
        var tick = _registry.Actions.Tick;
        foreach (var (player, _) in _requests.Where(request => request.Value.Until < tick).ToList())
        {
            _requests.Remove(player);
        }

        foreach (var session in _sessions.Values.Distinct().Where(session => !IsOpen(session)).ToList())
        {
            End(session, Cancelled);
        }
    }

    private bool IsOpen(Session session) =>
        session.Players.All(player => _options.Windows.Of(player)?.Kind == WindowKind.Trade);

    private Container? Inventory(string player) => _options.Bags.ContainerOf(player, ContainerId.Inventory);

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    /// <summary>
    /// A trade between two players: what each offers, whether it is at the confirm step, and who
    /// has accepted the step. An offer is a claim on what the player holds. Nothing moves until
    /// the swap.
    /// </summary>
    private sealed class Session(string a, string b)
    {
        private readonly Dictionary<string, OrderedDictionary<int, int>> _offers = new() { [a] = [], [b] = [] };
        private readonly HashSet<string> _accepted = [];

        public string A => a;

        public string B => b;

        public IReadOnlyList<string> Players { get; } = [a, b];

        public bool Confirming { get; private set; }

        public string Other(string player) => player == a ? b : a;

        /// <summary>How many of <paramref name="item"/> <paramref name="player"/> offers.</summary>
        public int Offered(string player, int item) => _offers[player].GetValueOrDefault(item);

        /// <summary>How many different items <paramref name="player"/> offers.</summary>
        public int LinesOf(string player) => _offers[player].Count;

        public bool HasAccepted(string player) => _accepted.Contains(player);

        public List<ItemStack> StacksOf(string player) => [.. _offers[player].Select(line => new ItemStack(line.Key, line.Value))];

        /// <summary>
        /// Sets what <paramref name="player"/> offers of <paramref name="item"/>. A count of zero
        /// takes it back. Both accepts go and the trade is back at the offer step.
        /// </summary>
        public void Offer(string player, int item, int count)
        {
            if (count == 0)
            {
                _offers[player].Remove(item);
            }
            else
            {
                _offers[player][item] = count;
            }

            Reset();
        }

        public void Reset()
        {
            _accepted.Clear();
            Confirming = false;
        }

        /// <summary>
        /// Records that <paramref name="player"/> accepted this step. The first step ends into
        /// the confirm step.
        /// </summary>
        /// <returns>Whether that finishes the confirm step.</returns>
        // F5: the caller needs to know when to make the swap.
        public bool Accept(string player)
        {
            _accepted.Add(player);
            if (_accepted.Count < 2)
            {
                return false;
            }

            _accepted.Clear();
            if (Confirming)
            {
                return true;
            }

            Confirming = true;
            return false;
        }
    }
}
