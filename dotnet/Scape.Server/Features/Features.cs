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
    Interactions Interactions)
{
    /// <summary>
    /// The one place features join the server. Each feature adds its action kinds, systems and
    /// components to <paramref name="registry"/> here. Systems in one phase run in the order their
    /// features register.
    /// </summary>
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
        var interactions = Interactions.Register(registry, services, new InteractionsOptions
        {
            Movement = movement,
            Objects = objects,
            Ground = ground,
            Items = bags,
        });
        return new Features(movement, social, bags, ground, npcs, actors, objects, interactions);
    }
}
