using Scape.Content;
using Scape.Core;

namespace Scape.Demo;

/// <summary>Someone on the island, and where they stood when the last tick began.</summary>
internal sealed class Actor(ulong id, Mover mover, ActorModel model, NpcSpawn? spawn = null)
{
    public ulong Id { get; } = id;

    public Mover Mover { get; } = mover;

    public ActorModel Model { get; } = model;

    public NpcSpawn? Spawn { get; } = spawn;

    public TileCoord From { get; private set; } = mover.Position;

    /// <summary>The turn about y that faces the last step, 0 for +z, which is north.</summary>
    public double Facing { get; private set; }

    /// <summary>Walking or running while it moved in the last tick, idle once it stood still.</summary>
    public ActorState State => Mover.Position == From
        ? ActorState.Idle
        : Mover.Speed == MoveSpeed.Run ? ActorState.Run : ActorState.Walk;

    /// <summary>Takes this tick's steps and turns to face the last of them.</summary>
    public void Advance(TileMap map)
    {
        From = Mover.Position;
        Mover.Advance(map);
        var steps = Mover.StepsLastTick;
        if (steps.Count > 0)
        {
            var before = steps.Count > 1 ? steps[^2] : From;
            Facing = Math.Atan2(steps[^1].X - before.X, steps[^1].Y - before.Y);
        }
    }
}

/// <summary>
/// Plane 0 of the starter island and who is on it. The ticks, paths and steps are the rules the
/// server runs. There is no server and no session.
/// </summary>
// shortcut: the rules run in the app with no session, until the session of row 4 replaces this loop.
internal sealed class Island
{
    private readonly Random _dice;

    public Island(Random dice)
    {
        _dice = dice;
        BlockSolidObjects();
        Player = new Actor(0, new Mover(Plan.Spawn), DefaultModels.All.Player(new Look(0xE0AC7E, 0x3B2A1E, 0x3E6FB0, new int[ItemDef.EquipSlots.Count])));
        Npcs = [.. Plan.Npcs.Select((spawn, i) => new Actor((ulong)i + 1, new Mover(spawn.Tile, Walker.Npc), ModelOf(spawn.Npc), spawn))];
    }

    public WorldPlan Plan { get; } = StarterIsland.Build();

    public Actor Player { get; }

    public IReadOnlyList<Actor> Npcs { get; }

    public IEnumerable<Actor> Actors => Npcs.Append(Player);

    /// <summary>One game tick: creatures with a radius may set off inside it, then everyone steps.</summary>
    public void Tick()
    {
        foreach (var npc in Npcs)
        {
            if (npc.Spawn is { Radius: > 0 } spawn && !npc.Mover.IsMoving && _dice.Next(5) == 0)
            {
                var goal = new TileCoord(
                    spawn.Tile.X + _dice.Next(-spawn.Radius, spawn.Radius + 1),
                    spawn.Tile.Y + _dice.Next(-spawn.Radius, spawn.Radius + 1),
                    0);
                npc.Mover.SetPath(PathSearch.Find(Plan.Map, npc.Mover.Position, goal, Walker.Npc, margin: 4).Steps);
            }
        }

        foreach (var actor in Actors)
        {
            actor.Advance(Plan.Map);
        }
    }

    public void WalkTo(TileCoord tile, MoveSpeed speed)
    {
        Player.Mover.Speed = speed;
        Player.Mover.SetPath(PathSearch.Find(Plan.Map, Player.Mover.Position, tile).Steps);
    }

    /// <summary>
    /// What is on <paramref name="tile"/> with the first thing to do to it, as "Chop down Tree",
    /// or "Walk here" for bare ground.
    /// </summary>
    // shortcut: the first entry of the client's menu, copied, until row 4b brings the menu.
    public string Hover(TileCoord tile)
    {
        if (Npcs.FirstOrDefault(n => n.Mover.Position == tile)?.Spawn is { } spawn)
        {
            var npc = DefaultNpcs.All.Require(spawn.Npc);
            var verb = npc.Fight is not null ? "Attack" : npc.Shop is not null ? "Trade" : npc.Dialogue is not null ? "Talk to" : null;
            return verb is null ? npc.Name : $"{verb} {npc.Name}";
        }

        var def = Plan.Objects
            .Where(p => DefaultObjects.All.Require(p.ObjectId).FootprintAt(p.Tile).Tiles.Contains(tile))
            .Select(p => DefaultObjects.All.Require(p.ObjectId))
            .FirstOrDefault();
        if (def is null)
        {
            return "Walk here";
        }

        var option = def.Options.FirstOrDefault(o => o.IsOfferedIn(0));
        return option is null ? def.Name : $"{option.Label} {def.Name}";
    }

    private static ActorModel ModelOf(int npc) =>
        DefaultModels.All.Npc(npc) ?? throw new InvalidOperationException($"Creature {npc} has no model.");

    // The server flags a solid object's tiles blocked. Doors stay open, since only the server opens one.
    private void BlockSolidObjects()
    {
        foreach (var placement in Plan.Objects.Where(p => p.ObjectId != ObjectId.Door))
        {
            var def = DefaultObjects.All.Require(placement.ObjectId);
            if (!def.BlocksIn(0))
            {
                continue;
            }

            foreach (var tile in def.FootprintAt(placement.Tile).Tiles)
            {
                Plan.Map.SetFlags(tile, Plan.Map.FlagsAt(tile) | TileFlags.Blocked);
            }
        }
    }
}
