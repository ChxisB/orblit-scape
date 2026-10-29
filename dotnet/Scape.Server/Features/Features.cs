using Scape.Core;

namespace Scape.Server;

/// <summary>What the rest of the server may reach into after the features register.</summary>
internal sealed record Features(
    Movement Movement,
    Social Social,
    Items Items,
    GroundItems Ground,
    Npcs Npcs,
    Actors Actors,
    WorldObjects Objects,
    Skills Skills,
    Interactions Interactions)
{
    /// <summary>
    /// The one place features join the server. Each feature adds its action kinds, systems and
    /// components to <paramref name="registry"/> here. Systems in one phase run in the order their
    /// features register.
    /// </summary>
    /// <exception cref="ArgumentException">A creature keeps a shop and the options have no shop rules.</exception>
    public static Features Register(Registry registry, Services services, ScapeServerOptions options)
    {
        var movement = Movement.Register(registry, services, options.Spawn);
        var social = Social.Register(registry, services, movement.Standing);
        var bags = Items.Register(registry, services, options.Items);
        var ground = GroundItems.Register(registry, services, new GroundItemsOptions
        {
            Items = options.Items,
            TileOf = movement.TileOf,
            InventoryOf = player => bags.ContainerOf(player, ContainerId.Inventory),
        });
        var npcs = Npcs.Register(registry, services, new NpcsOptions
        {
            Npcs = options.Npcs,
            PlayerTiles = () => movement.Tiles,
            Random = options.Random,
        });
        var actors = Actors.Register(registry, services, new ActorsOptions { Movement = movement, Bags = bags, Npcs = npcs });
        var objects = WorldObjects.Register(registry, services, options.Objects);
        var skills = Skills.Register(registry, services, options.Skills);
        var interactions = Interactions.Register(registry, services, new InteractionsOptions
        {
            Movement = movement,
            Objects = objects,
            Ground = ground,
            Items = bags,
            Npcs = npcs,
        });
        var features = new Features(movement, social, bags, ground, npcs, actors, objects, skills, interactions);
        features.RegisterMaking(registry, services, options);
        features.RegisterWindows(registry, services, options);
        return features;
    }

    private void RegisterMaking(Registry registry, Services services, ScapeServerOptions options)
    {
        Gathering.Register(registry, services, new GatheringOptions
        {
            Interactions = Interactions,
            Nodes = options.Nodes,
            Items = options.Items,
            Objects = Objects,
            Skills = Skills,
            Bags = Items,
            Movement = Movement,
            Actors = Actors,
            Random = options.Random,
        });
        if (options.Recipes is { } recipes)
        {
            Production.Register(registry, services, new ProductionOptions
            {
                Interactions = Interactions,
                Recipes = recipes,
                Items = options.Items,
                Objects = Objects,
                Skills = Skills,
                Bags = Items,
                Movement = Movement,
                Actors = Actors,
                Random = options.Random,
            });
        }
    }

    private void RegisterWindows(Registry registry, Services services, ScapeServerOptions options)
    {
        var windows = Windows.Register(registry, services, Movement);
        Bank.Register(registry, services, new BankOptions { Interactions = Interactions, Bags = Items, Windows = windows });
        if (options.Shops is { } rules)
        {
            Shops.Register(registry, services, new ShopsOptions
            {
                Rules = rules,
                Items = options.Items,
                Npcs = options.Npcs,
                Interactions = Interactions,
                Bags = Items,
                Windows = windows,
            });
        }
        else if (options.Npcs.Npcs.Any(npc => npc.Shop is not null))
        {
            throw new ArgumentException("A creature keeps a shop, so the server needs shop rules.", nameof(options));
        }
    }
}
