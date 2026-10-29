using Scape.Core;

namespace Scape.Server;

/// <summary>What interactions ask of the rest of the server.</summary>
public sealed record InteractionsOptions
{
    public required Movement Movement { get; init; }

    public required WorldObjects Objects { get; init; }

    public required GroundItems Ground { get; init; }

    public required Items Items { get; init; }
}

/// <summary>
/// Clicking a thing does something. An interaction walks the player to where they can reach it,
/// then runs what the click names. A walk, or another click, cancels one that has not yet run.
/// </summary>
public sealed class Interactions
{
    private const string CannotReach = "You cannot reach that.";
    private const string NothingHappens = "Nothing interesting happens.";

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly InteractionsOptions _options;
    private readonly ActionKind _interact = new("interact", cancels: new CancelsAll());
    private readonly Dictionary<string, Action<string, PlacedObject>> _objectActions = [];
    private readonly Dictionary<(int Item, int Object), Action<string, int, PlacedObject>> _useOnObject = [];
    private readonly Dictionary<(int, int), Action<string, int, int>> _useOnItem = [];

    private Interactions(Registry registry, Services services, InteractionsOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
    }

    /// <summary>
    /// Adds the interactions to <paramref name="registry"/> and returns the feature, so a feature
    /// that owns an action can register its handler.
    /// </summary>
    public static Interactions Register(Registry registry, Services services, InteractionsOptions options)
    {
        var feature = new Interactions(registry, services, options);
        registry.AddKind(feature._interact);
        registry.AddIntentHandler(feature.OnIntent);
        feature.AddObjectAction("toggle", (_, placed) => feature.Toggle(placed));
        feature.AddObjectAction("climb", feature.Climb);
        return feature;
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the player and the object when a player picks an option
    /// of an object whose definition names <paramref name="name"/>.
    /// </summary>
    /// <exception cref="ArgumentException">An action is already registered under the name.</exception>
    public void AddObjectAction(string name, Action<string, PlacedObject> action)
    {
        if (!_objectActions.TryAdd(name, action))
        {
            throw new ArgumentException($"{name} is already registered.", nameof(name));
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with the player, the slot and the object when
    /// <paramref name="item"/> is used on an object whose definition is <paramref name="objectId"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The use is already registered.</exception>
    public void AddUseOnObject(int item, int objectId, Action<string, int, PlacedObject> action)
    {
        if (!_useOnObject.TryAdd((item, objectId), action))
        {
            throw new ArgumentException("That use is already registered.", nameof(item));
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> when <paramref name="item"/> is used on
    /// <paramref name="onItem"/>, in either order. It is told the player, the slot of
    /// <paramref name="item"/>, then the slot of <paramref name="onItem"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The use is already registered.</exception>
    public void AddUseOnItem(int item, int onItem, Action<string, int, int> action)
    {
        void InOrder(string player, int slot, int onSlot)
        {
            var first = Held(player, slot)?.Item == item;
            action(player, first ? slot : onSlot, first ? onSlot : slot);
        }

        if (!_useOnItem.TryAdd(Pair(item, onItem), InOrder))
        {
            throw new ArgumentException("That use is already registered.", nameof(item));
        }
    }

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    private static string PickUpNotice(Outcome outcome) => outcome switch
    {
        Outcome.Partly => "You only have room for some of it.",
        Outcome.WontFit => "You have no room for that.",
        Outcome.NothingThere => "It is gone.",
        _ => CannotReach,
    };

    private void OnIntent(string player, Intent intent)
    {
        if (intent is not Interact click)
        {
            return;
        }

        switch (click.Kind)
        {
            case InteractKind.PlacedObject:
                ClickObject(player, click);
                break;
            case InteractKind.Ground:
                ClickGround(player, click);
                break;
            case InteractKind.UseOnObject:
                ClickUseOnObject(player, click);
                break;
            case InteractKind.UseOnItem:
                Submit(player, _ => RunUseOnItem(player, click));
                break;
        }
    }

    private void ClickObject(string player, Interact click)
    {
        if (_options.Objects.Find((ulong)click.Id) is not { } placed || click.Option >= placed.Def.Options.Count)
        {
            return;
        }

        Start(player, new Beside(placed.Footprint), () => RunOption(player, placed, click.Option));
    }

    private void ClickGround(string player, Interact click)
    {
        var networkId = (ulong)click.Id;
        if (_options.Ground.WhereIs(player, networkId) is { } tile)
        {
            Start(player, new OnTile(tile), () => RunTake(player, networkId));
        }
    }

    private void ClickUseOnObject(string player, Interact click)
    {
        if (_options.Objects.Find((ulong)click.Id) is not { } placed || Held(player, click.Option) is null)
        {
            return;
        }

        Start(player, new Beside(placed.Footprint), () => RunUseOnObject(player, click.Option, placed));
    }

    private void RunOption(string player, PlacedObject placed, int index)
    {
        var option = placed.Def.Options[index];
        if (!_objectActions.TryGetValue(option.Action, out var action) || !option.IsOfferedIn(placed.State))
        {
            Tell(player, NothingHappens);
            return;
        }

        action(player, placed);
    }

    private void RunTake(string player, ulong networkId)
    {
        var outcome = _options.Ground.PickUp(player, networkId);
        if (outcome != Outcome.Done)
        {
            Tell(player, PickUpNotice(outcome));
        }
    }

    private void RunUseOnObject(string player, int slot, PlacedObject placed)
    {
        if (Held(player, slot) is not { } held || !_useOnObject.TryGetValue((held.Item, placed.Def.Id), out var action))
        {
            Tell(player, NothingHappens);
            return;
        }

        action(player, slot, placed);
    }

    private void RunUseOnItem(string player, Interact click)
    {
        var slot = click.Option;

        // An id past int.MaxValue casts to a negative slot, which holds nothing.
        var onSlot = (int)click.Id;
        if (slot == onSlot
            || Held(player, slot) is not { } first
            || Held(player, onSlot) is not { } second
            || !_useOnItem.TryGetValue(Pair(first.Item, second.Item), out var action))
        {
            Tell(player, NothingHappens);
            return;
        }

        action(player, slot, onSlot);
    }

    private void Toggle(PlacedObject placed) => _options.Objects.SetState(placed, placed.State == 0 ? 1 : 0);

    private void Climb(string player, PlacedObject placed)
    {
        if (placed.Destination is not { } to)
        {
            Tell(player, NothingHappens);
            return;
        }

        _options.Movement.Teleport(player, to);
    }

    private ItemStack? Held(string player, int slot) =>
        _options.Items.ContainerOf(player, ContainerId.Inventory)?.At(slot);

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    /// <summary>Queues <paramref name="run"/> under the actor of <paramref name="player"/>, cancelling what that actor had waiting.</summary>
    private void Submit(string player, Action<ulong> run)
    {
        if (_options.Movement.ActorOf(player) is { } actor)
        {
            _registry.Actions.Submit(actor, _interact, () => run(actor));
        }
    }

    /// <summary>Walks <paramref name="player"/> to where <paramref name="reach"/> holds, then runs <paramref name="run"/>.</summary>
    private void Start(string player, Reach reach, Action run) => Submit(player, actor => Begin(player, actor, reach, run));

    private void Begin(string player, ulong actor, Reach reach, Action run)
    {
        if (_options.Movement.TileOf(player) is not { } tile)
        {
            return;
        }

        _options.Movement.Follow(player, []);
        if (tile.Plane != reach.Target.Origin.Plane)
        {
            Tell(player, CannotReach);
        }
        else if (reach.IsSatisfied(_services.Map, tile))
        {
            run();
        }
        else
        {
            Approach(player, actor, reach, run);
        }
    }

    private void Approach(string player, ulong actor, Reach reach, Action run)
    {
        // Begin has just read it.
        var tile = _options.Movement.TileOf(player)!.Value;
        var path = PathSearch.FindToReach(_services.Map, tile, reach);
        if (!path.Reached)
        {
            Tell(player, CannotReach);
            return;
        }

        _options.Movement.Follow(player, path.Steps);
        Wait(player, actor, reach, run);
    }

    /// <summary>Checks again next tick, queued so that a walk or a click cancels it.</summary>
    private void Wait(string player, ulong actor, Reach reach, Action run) =>
        _registry.Actions.Submit(actor, _interact, () => Arrive(player, actor, reach, run));

    private void Arrive(string player, ulong actor, Reach reach, Action run)
    {
        if (_options.Movement.TileOf(player) is not { } tile)
        {
            return;
        }

        if (reach.IsSatisfied(_services.Map, tile))
        {
            run();
        }
        else if (_options.Movement.IsMoving(player))
        {
            Wait(player, actor, reach, run);
        }
        else
        {
            Tell(player, CannotReach);
        }
    }
}
