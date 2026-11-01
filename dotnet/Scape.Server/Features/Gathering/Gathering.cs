using Scape.Core;

namespace Scape.Server;

/// <summary>What gathering asks of the rest of the server.</summary>
public sealed record GatheringOptions
{
    /// <summary>Decides when gathering starts, since it is the <c>gather</c> option of an object.</summary>
    public required Interactions Interactions { get; init; }

    public required NodeSet Nodes { get; init; }

    public required ItemSet Items { get; init; }

    public required WorldObjects Objects { get; init; }

    public required Skills Skills { get; init; }

    public required Items Bags { get; init; }

    public required Movement Movement { get; init; }

    public required Actors Actors { get; init; }

    public required Random Random { get; init; }
}

/// <summary>
/// Chopping, mining and fishing. A player who picks a node's option keeps trying every
/// <see cref="Ticks"/> until the bag is full, the node runs dry or they do something else. A dry
/// node is bare for the ticks its definition names and then fills again.
/// </summary>
public sealed class Gathering
{
    /// <summary>Ticks between one try and the next.</summary>
    public const int Ticks = 4;

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly GatheringOptions _options;
    private readonly ActionKind _gather = new("gather", Ticks);

    // A tick, not a count down, so a node in a cell nobody is near fills on the first tick someone is.
    private readonly Dictionary<PlacedObject, long> _fillsAt = [];

    private Gathering(Registry registry, Services services, GatheringOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
    }

    /// <summary>Adds gathering to <paramref name="registry"/>.</summary>
    public static void Register(Registry registry, Services services, GatheringOptions options)
    {
        var feature = new Gathering(registry, services, options);
        options.Interactions.AddObjectAction("gather", feature.Begin);
        registry.AddKind(feature._gather);
        registry.AddSystem(Phase.Npcs, feature.Refill);
    }

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    private void Begin(string player, PlacedObject placed)
    {
        if (_options.Nodes.Find(placed.Def.Id) is not { } node)
        {
            return;
        }

        if (Refusal(player, node, placed) is { } refusal)
        {
            Tell(player, refusal);
            return;
        }

        Queue(player, node, placed);
    }

    private void Queue(string player, NodeDef node, PlacedObject placed)
    {
        if (_options.Movement.ActorOf(player) is { } actor)
        {
            _registry.Actions.Submit(actor, _gather, () => Try(player, node, placed));
        }
    }

    private void Try(string player, NodeDef node, PlacedObject placed)
    {
        if (_options.Movement.ActorOf(player) is not { } actor)
        {
            return;
        }

        if (Refusal(player, node, placed) is { } refusal)
        {
            Tell(player, refusal);
            return;
        }

        _options.Actors.Play(actor, ActorState.Gather);
        var level = _options.Skills.LevelOf(player, node.Skill);
        if (_options.Random.NextDouble() < node.ChanceAt(level))
        {
            Succeed(player, node, placed);
        }

        if (placed.State == NodeState.Full)
        {
            Queue(player, node, placed);
        }
    }

    private void Succeed(string player, NodeDef node, PlacedObject placed)
    {
        // Refusal checked the inventory can hold it.
        _options.Bags.ContainerOf(player, ContainerId.Inventory)!.Add(node.Product, 1);
        _options.Skills.Award(player, node.Skill, node.Xp);
        if (_options.Random.NextDouble() < node.DepleteChance)
        {
            _options.Objects.SetState(placed, NodeState.Depleted);
            _fillsAt[placed] = _registry.Actions.Tick + node.RespawnTicks;
        }
    }

    /// <summary>Why <paramref name="player"/> cannot gather from <paramref name="placed"/> now, or null when they can.</summary>
    private string? Refusal(string player, NodeDef node, PlacedObject placed)
    {
        if (placed.State != NodeState.Full)
        {
            return "There is nothing left.";
        }

        if (_options.Skills.LevelOf(player, node.Skill) < node.Level)
        {
            return FormattableString.Invariant($"You need level {node.Level} in {node.Skill} to do that.");
        }

        if (!Holds(player, node.Tool))
        {
            return $"You need a {_options.Items.Require(node.Tool).Name.ToLowerInvariant()} to do that.";
        }

        var inventory = _options.Bags.ContainerOf(player, ContainerId.Inventory);
        return inventory is null || !inventory.CanHold(node.Product) ? "Your inventory is full." : null;
    }

    private bool Holds(string player, int tool) =>
        new[] { ContainerId.Inventory, ContainerId.Equipment }
            .Any(id => _options.Bags.ContainerOf(player, id)?.CountOf(tool) > 0);

    private void Refill()
    {
        var tick = _registry.Actions.Tick;
        foreach (var (placed, due) in _fillsAt.ToList())
        {
            var origin = placed.Footprint.Origin;
            if (due <= tick && _services.Active.IsActiveAt(origin.X, origin.Y))
            {
                _options.Objects.SetState(placed, NodeState.Full);
                _fillsAt.Remove(placed);
            }
        }
    }
}
