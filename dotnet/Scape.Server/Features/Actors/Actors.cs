using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>What actors ask of the rest of the server.</summary>
public sealed record ActorsOptions
{
    public required Movement Movement { get; init; }

    public required Items Bags { get; init; }

    public required Npcs Npcs { get; init; }
}

/// <summary>
/// What every actor in view looks like and is doing: the <see cref="Look"/> of a player, from
/// what they wear, and the <see cref="Anim"/> of a player or a creature, from how it moved this
/// tick.
/// </summary>
/// <remarks>The server sends item ids and states. The client builds the parts and plays the clips.</remarks>
public sealed class Actors
{
    private static readonly int[] Skins = [0xF1C27D, 0xE0AC69, 0xC68642, 0x8D5524, 0x5C3A1E];
    private static readonly int[] Hairs = [0x0F0A07, 0x3B2F2F, 0x6A4A2A, 0xB8863B, 0xA0A0A0];
    private static readonly int[] Cloths = [0x2F6FDB, 0xB23A3A, 0x3A8F4F, 0x8A5BB5, 0xC9A227, 0x3D7F8F];

    private readonly World _world;
    private readonly ActorsOptions _options;
    private readonly ComponentType _look;
    private readonly ComponentType _anim;

    private Actors(World world, ActorsOptions options)
    {
        _world = world;
        _options = options;
        _look = world.RegisterComponent(Look.Name, ComponentKind.Int32, Look.Ints);
        _anim = world.RegisterComponent(Anim.Name, ComponentKind.Int32, Anim.Ints);
    }

    /// <summary>Adds the components to <paramref name="registry"/> and returns the feature.</summary>
    public static Actors Register(Registry registry, Services services, ActorsOptions options)
    {
        var feature = new Actors(services.World, options);
        registry.Replicate(feature._look);
        registry.Replicate(feature._anim);
        registry.AddJoinHandler(feature.Join);
        registry.AddSystem(Phase.Outbound, feature.Sync);
        options.Npcs.AddStandHandler(feature.Stood);
        return feature;
    }

    /// <summary>FNV-1a, because the string hash may differ between runs.</summary>
    private static uint Hash(string text)
    {
        var hash = 0x811C9DC5u;
        foreach (var unit in text)
        {
            hash = unchecked((hash ^ unit) * 0x01000193u);
        }

        return hash;
    }

    // shortcut: colours come from the name, so a player looks the same each login but cannot
    // choose. A character screen saves a choice later.
    private static Look LookOf(string player, IReadOnlyList<int> worn)
    {
        var hash = Hash(player);
        return new Look(
            Skins[hash % Skins.Length],
            Hairs[hash / 5 % Hairs.Length],
            Cloths[hash / 25 % Cloths.Length],
            worn);
    }

    private void Join(string player)
    {
        // Movement joined them first.
        var entity = _options.Movement.ActorOf(player)!.Value;
        _world.Add(entity, _look);
        _world.Add(entity, _anim);
        if (_world.TryGetBytes(entity, _anim, out var anim))
        {
            Anim.Write(anim, new Anim());
        }

        if (_world.TryGetBytes(entity, _look, out var look))
        {
            Look.Write(look, LookOf(player, Worn(player)));
        }
    }

    private void Stood(PlacedNpc npc)
    {
        _world.Add(npc.Entity, _anim);
        if (_world.TryGetBytes(npc.Entity, _anim, out var bytes))
        {
            Anim.Write(bytes, new Anim());
        }
    }

    // Runs after movement and creatures have stepped, so the resting state is how each actor
    // moved this tick.
    private void Sync()
    {
        var movement = _options.Movement;
        foreach (var player in movement.Players)
        {
            // It is in the world.
            var entity = movement.ActorOf(player)!.Value;
            Rest(entity, movement.StepsLastTick(player));
            Dress(player, entity);
        }

        foreach (var npc in _options.Npcs.Standing)
        {
            Rest(npc.Entity, npc.StepsLastTick);
        }
    }

    private void Rest(ulong entity, int steps)
    {
        if (!_world.TryGetBytes(entity, _anim, out var bytes))
        {
            return;
        }

        var now = Anim.TryRead(bytes) ?? new Anim();

        // Nothing sets a run speed yet, so run waits for run energy.
        var resting = steps switch
        {
            0 => ActorState.Idle,
            1 => ActorState.Walk,
            _ => ActorState.Run,
        };
        if (now.Base != resting)
        {
            Anim.Write(bytes, now.WithBase(resting));
        }
    }

    // shortcut: this reads every player's look every tick. Move it to an equipment change hook
    // when a tick with many players measures slow.
    private void Dress(string player, ulong entity)
    {
        if (!_world.TryGetBytes(entity, _look, out var bytes) || Look.TryRead(bytes) is not { } now)
        {
            return;
        }

        var next = now.WithWorn(Worn(player));
        if (!next.Equals(now))
        {
            Look.Write(bytes, next);
        }
    }

    /// <summary>The item in each equipment slot of <paramref name="player"/>, 0 for an empty one.</summary>
    private int[] Worn(string player)
    {
        var worn = _options.Bags.ContainerOf(player, ContainerId.Equipment);
        return [.. Enumerable.Range(0, ItemDef.EquipSlots.Count).Select(slot => worn?.At(slot)?.Item ?? 0)];
    }
}
