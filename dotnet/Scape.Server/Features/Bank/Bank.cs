using Scape.Core;

namespace Scape.Server;

/// <summary>What the bank asks of the rest of the server.</summary>
public sealed record BankOptions
{
    /// <summary>Decides when the bank opens, since it is the <c>bank</c> option of an object.</summary>
    public required Interactions Interactions { get; init; }

    public required Items Bags { get; init; }

    public required Windows Windows { get; init; }
}

/// <summary>
/// The bank booth. Using one opens the player's bank, and while it is open they can move stacks
/// between the bank and the inventory.
/// </summary>
public sealed class Bank
{
    private const string NothingThere = "There is nothing in that slot.";

    private readonly Services _services;
    private readonly BankOptions _options;

    private Bank(Services services, BankOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>Adds the bank to <paramref name="registry"/>.</summary>
    public static void Register(Registry registry, Services services, BankOptions options)
    {
        var feature = new Bank(services, options);
        options.Interactions.AddObjectAction("bank", feature.Open);
        registry.AddIntentHandler(feature.OnIntent);
    }

    private static string? NoticeOf(Outcome outcome) => outcome switch
    {
        Outcome.WontFit => "There is no room for that.",
        Outcome.NothingThere or Outcome.NoSuchSlot => NothingThere,
        _ => null,
    };

    private void Open(string player, PlacedObject booth)
    {
        _options.Windows.Open(player, new OpenWindow(WindowKind.Bank, 0, new Beside(booth.Footprint)));
        _services.Hosting.Tell(player, new BankOpen());
    }

    private void OnIntent(string player, Intent intent)
    {
        var bags = _options.Bags;
        if (_options.Windows.Of(player)?.Kind != WindowKind.Bank
            || bags.ContainerOf(player, ContainerId.Inventory) is not { } inventory
            || bags.ContainerOf(player, ContainerId.Bank) is not { } bank)
        {
            return;
        }

        // The wire holds a count between 1 and ItemStack.MaxCount, so it fits an int.
        switch (intent)
        {
            case Deposit deposit:
                Report(player, inventory.Transfer(deposit.Slot, bank, (int)deposit.Count, deposit.Tab));
                break;
            case Withdraw withdraw:
                Report(player, bank.Transfer(withdraw.Slot, inventory, (int)withdraw.Count));
                break;
        }
    }

    private void Report(string player, Outcome outcome)
    {
        if (NoticeOf(outcome) is { } notice)
        {
            _services.Hosting.Tell(player, new Notice(notice));
        }
    }
}
