using Scape.Core;

namespace Scape.Server;

/// <summary>What production asks of the rest of the server.</summary>
public sealed record ProductionOptions
{
    /// <summary>Decides when a make starts, since each recipe is a use on an item or an object.</summary>
    public required Interactions Interactions { get; init; }

    public required RecipeSet Recipes { get; init; }

    public required ItemSet Items { get; init; }

    public required WorldObjects Objects { get; init; }

    public required Skills Skills { get; init; }

    public required Items Bags { get; init; }

    public required Movement Movement { get; init; }

    public required Actors Actors { get; init; }

    public required Random Random { get; init; }
}

/// <summary>
/// Cooking, smithing, fletching and lighting fires. A player who uses one item on another, or on a
/// station, keeps making what the recipe names every <see cref="Recipe.Ticks"/> until they run out
/// of inputs, the make is refused or they do something else. A lit fire is a placed object that
/// goes out after the ticks its recipe names.
/// </summary>
public sealed class Production
{
    private const string NoRoom = "You have no room for that.";

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly ProductionOptions _options;

    // One kind for each number of ticks a make can take, since a kind has one delay.
    private readonly Dictionary<int, ActionKind> _kinds = [];
    private readonly Dictionary<PlacedObject, long> _goesOutAt = [];

    private Production(Registry registry, Services services, ProductionOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
    }

    /// <summary>Adds the recipes to <paramref name="registry"/>.</summary>
    public static void Register(Registry registry, Services services, ProductionOptions options)
    {
        var feature = new Production(registry, services, options);
        foreach (var recipe in options.Recipes.Recipes)
        {
            feature.Listen(recipe);
        }

        foreach (var ticks in options.Recipes.Recipes.Select(recipe => recipe.Ticks).Distinct())
        {
            var kind = new ActionKind(FormattableString.Invariant($"make{ticks}"), ticks);
            registry.AddKind(kind);
            feature._kinds[ticks] = kind;
        }

        registry.AddSystem(Phase.Npcs, feature.BurnOut);
    }

    private void Listen(Recipe recipe)
    {
        switch (recipe.Start)
        {
            case OnItem start:
                _options.Interactions.AddUseOnItem(start.Item, start.Target, (player, _, _) => Begin(player, recipe, null));
                break;
            case OnObject start:
                _options.Interactions.AddUseOnObject(start.Item, start.ObjectId, (player, _, station) => Begin(player, recipe, station));
                break;
        }
    }

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    private void Begin(string player, Recipe recipe, PlacedObject? station)
    {
        if (Refusal(player, recipe, station) is { } refusal)
        {
            Tell(player, refusal);
            return;
        }

        Queue(player, recipe, station);
    }

    private void Queue(string player, Recipe recipe, PlacedObject? station)
    {
        if (_options.Movement.ActorOf(player) is { } actor)
        {
            _registry.Actions.Submit(actor, _kinds[recipe.Ticks], () => Make(player, recipe, station));
        }
    }

    private void Make(string player, Recipe recipe, PlacedObject? station)
    {
        if (_options.Movement.ActorOf(player) is not { } actor)
        {
            return;
        }

        if (Refusal(player, recipe, station) is { } refusal)
        {
            Tell(player, refusal);
            return;
        }

        _options.Actors.Play(actor, ActorState.Make);
        var level = _options.Skills.LevelOf(player, recipe.Skill);
        var failed = _options.Random.NextDouble() < recipe.FailChanceAt(level);
        var done = failed ? Fail(player, recipe) : Succeed(player, recipe);
        if (done && Refusal(player, recipe, station) is null)
        {
            Queue(player, recipe, station);
        }
    }

    /// <summary>Uses the inputs and gives the failure's item.</summary>
    /// <returns>Whether it did.</returns>
    private bool Fail(string player, Recipe recipe)
    {
        ItemStack[] give = recipe.Failure?.Item is { } item ? [new ItemStack(item, 1)] : [];
        if (Bag(player).Exchange(recipe.Inputs, give) != Outcome.Done)
        {
            Tell(player, NoRoom);
            return false;
        }

        Tell(player, $"You fail to {recipe.Name}.");
        return true;
    }

    /// <summary>Uses the inputs and gives the product and experience.</summary>
    /// <returns>Whether it did.</returns>
    private bool Succeed(string player, Recipe recipe)
    {
        ItemStack[] give = recipe.Product is Gives gives ? [gives.Stack] : [];
        if (Bag(player).Exchange(recipe.Inputs, give) != Outcome.Done)
        {
            Tell(player, NoRoom);
            return false;
        }

        if (recipe.Product is Lights lights)
        {
            // Refusal read the tile.
            var fire = _options.Objects.Place(lights.ObjectId, _options.Movement.TileOf(player)!.Value);
            _goesOutAt[fire] = _registry.Actions.Tick + lights.Ticks;
        }

        _options.Skills.Award(player, recipe.Skill, recipe.Xp);
        return true;
    }

    /// <summary>Why <paramref name="player"/> cannot make <paramref name="recipe"/> now, or null when they can.</summary>
    private string? Refusal(string player, Recipe recipe, PlacedObject? station)
    {
        if (_options.Movement.TileOf(player) is not { } tile)
        {
            return "You cannot do that.";
        }

        if (station is not null && _options.Objects.Find(station.NetworkId) is null)
        {
            return $"The {station.Def.Name.ToLowerInvariant()} has gone.";
        }

        if (Lacks(player, recipe) is { } lack)
        {
            return lack;
        }

        return recipe.Product is Lights lights && !_options.Objects.CanPlace(lights.ObjectId, tile)
            ? "You cannot light a fire here."
            : null;
    }

    /// <summary>The level, tool or inputs <paramref name="player"/> lacks for <paramref name="recipe"/>, or null.</summary>
    private string? Lacks(string player, Recipe recipe)
    {
        if (_options.Skills.LevelOf(player, recipe.Skill) < recipe.Level)
        {
            return FormattableString.Invariant($"You need level {recipe.Level} in {recipe.Skill} to do that.");
        }

        if (recipe.Tool is { } tool && !Holds(player, tool))
        {
            return $"You need a {_options.Items.Require(tool).Name.ToLowerInvariant()} to do that.";
        }

        return recipe.Inputs.All(stack => Bag(player).CountOf(stack.Item) >= stack.Count)
            ? null
            : "You do not have what you need.";
    }

    // Every player in the world has one.
    private Container Bag(string player) => _options.Bags.ContainerOf(player, ContainerId.Inventory)!;

    private bool Holds(string player, int tool) =>
        new[] { ContainerId.Inventory, ContainerId.Equipment }
            .Any(id => _options.Bags.ContainerOf(player, id)?.CountOf(tool) > 0);

    private void BurnOut()
    {
        var tick = _registry.Actions.Tick;
        foreach (var (fire, due) in _goesOutAt.ToList())
        {
            if (due <= tick)
            {
                _options.Objects.Remove(fire);
                _goesOutAt.Remove(fire);
            }
        }
    }
}
