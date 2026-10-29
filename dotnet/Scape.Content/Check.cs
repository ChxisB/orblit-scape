using System.Globalization;
using Scape.Core;

namespace Scape.Content;

/// <summary>The data files of a game, which <see cref="ContentCheck"/> checks against one another.</summary>
public sealed record ContentSet
{
    public required ItemSet Items { get; init; }

    public required ObjectSet Objects { get; init; }

    public required NodeSet Nodes { get; init; }

    public required NpcSet Npcs { get; init; }

    public required RecipeSet Recipes { get; init; }

    public required Story Story { get; init; }

    public required WorldPlan World { get; init; }
}

/// <summary>Finds where the data files disagree with one another.</summary>
public static class ContentCheck
{
    private static readonly (int Dx, int Dy)[] Straight = [(0, -1), (-1, 0), (1, 0), (0, 1)];
    private static readonly (int Dx, int Dy)[] Diagonal = [(-1, -1), (1, -1), (-1, 1), (1, 1)];
    private static readonly (int Dx, int Dy)[] Around = [.. Straight, .. Diagonal];

    /// <summary>
    /// Every way the data files disagree with one another, one line each. Empty
    /// when they agree. Each file is checked alone when it is built, so this is
    /// what is left: a name in one file that no other file answers.
    /// </summary>
    /// <remarks>
    /// Run it before the server stocks the plan's map. The server flags the tiles
    /// of a solid object blocked, and this check would then report every one.
    /// </remarks>
    public static IReadOnlyList<string> Problems(ContentSet content) =>
    [
        .. DialoguesAnswered(content.Npcs, content.Story),
        .. StagesReached(content.Story),
        .. ItemsExist(content.Items, content.Npcs, content.Story),
        .. ItemsObtainable(content),
        .. ObjectsStand(content.Objects, content.Recipes, content.World),
        .. NpcsStand(content.Npcs, content.World),
        .. Ground(content.Objects, content.Npcs, content.World),
        .. Reach(content.Objects, content.World),
    ];

    private static string Quote(string text) => $"'{text}'";

    private static string Where(TileCoord tile) => FormattableString.Invariant($"({tile.X}, {tile.Y}, {tile.Plane})");

    private static IEnumerable<string> DialoguesAnswered(NpcSet npcs, Story story)
    {
        var keys = story.Dialogues.Select(d => d.Key).ToHashSet();
        var opened = npcs.Npcs.Select(n => n.Dialogue).OfType<string>().ToHashSet();
        foreach (var npc in npcs.Npcs)
        {
            if (npc.Dialogue is { } key && !keys.Contains(key))
            {
                yield return $"{npc.Name} opens dialogue {Quote(key)}, which does not exist.";
            }
        }

        foreach (var dialogue in story.Dialogues)
        {
            if (!opened.Contains(dialogue.Key))
            {
                yield return $"Dialogue {Quote(dialogue.Key)} is opened by no creature.";
            }

            foreach (var name in dialogue.BeforeNames.Where(n => !story.Steps.ContainsKey(n)))
            {
                yield return $"Dialogue {Quote(dialogue.Key)} runs step {Quote(name)}, which the story does not define.";
            }
        }
    }

    /// <summary>
    /// A stage that no step a dialogue runs moves a player to is a quest that
    /// cannot be finished.
    /// </summary>
    private static IEnumerable<string> StagesReached(Story story)
    {
        var reached = story.Dialogues
            .SelectMany(d => d.BeforeNames)
            .Where(story.Steps.ContainsKey)
            .Select(name => story.Steps[name])
            .ToHashSet();
        foreach (var quest in story.Book.Quests)
        {
            for (var stage = 1; stage <= quest.Last; stage++)
            {
                if (!reached.Contains(new QuestStep(quest.Key, stage)))
                {
                    yield return FormattableString.Invariant(
                        $"Quest {Quote(quest.Key)} stage {stage} is reached by no step that a dialogue runs.");
                }
            }
        }
    }

    /// <summary>The items a creature drops, a shop sells or a quest gives.</summary>
    private static IEnumerable<(string Who, int Item)> Named(NpcSet npcs, Story story)
    {
        foreach (var npc in npcs.Npcs)
        {
            foreach (var item in npc.Loot?.Items ?? [])
            {
                yield return ($"{npc.Name} drops", item);
            }

            foreach (var line in npc.Shop?.Lines ?? [])
            {
                yield return ($"{npc.Name} sells", line.Item);
            }
        }

        foreach (var (quest, reward) in story.Rewards)
        {
            foreach (var stack in reward.Items)
            {
                yield return ($"Quest {Quote(quest)} gives", stack.Item);
            }
        }
    }

    private static IEnumerable<string> ItemsExist(ItemSet items, NpcSet npcs, Story story) =>
        Named(npcs, story)
            .Where(n => items.Find(n.Item) is null)
            .Select(n => FormattableString.Invariant($"{n.Who} item {n.Item}, which does not exist."));

    private static IEnumerable<int> Needs(Recipe recipe)
    {
        if (recipe.Tool is { } tool)
        {
            yield return tool;
        }

        foreach (var stack in recipe.Inputs)
        {
            yield return stack.Item;
        }
    }

    private static IEnumerable<int> Made(Recipe recipe)
    {
        if (recipe.Product is Gives gives)
        {
            yield return gives.Stack.Item;
        }

        if (recipe.Failure?.Item is { } item)
        {
            yield return item;
        }
    }

    /// <summary>
    /// What a player can end up holding. It starts with what shops sell,
    /// creatures drop and quests give, then adds what a node or a recipe makes
    /// once its tool and inputs are held. A recipe that needs its own product
    /// adds nothing.
    /// </summary>
    private static HashSet<int> Obtainable(ContentSet content)
    {
        var got = Named(content.Npcs, content.Story).Select(n => n.Item).ToHashSet();
        int before;
        do
        {
            before = got.Count;

            // The collection expression is filled before UnionWith changes the set it reads.
            got.UnionWith(
            [
                .. content.Nodes.Nodes.Where(n => got.Contains(n.Tool)).Select(n => n.Product),
                .. content.Recipes.Recipes.Where(r => Needs(r).All(got.Contains)).SelectMany(Made),
            ]);
        }
        while (got.Count > before);
        return got;
    }

    /// <summary>An item that a node or a recipe asks the player to hold must be obtainable.</summary>
    private static IEnumerable<string> ItemsObtainable(ContentSet content)
    {
        var got = Obtainable(content);
        var needed = content.Nodes.Nodes.Select(n => n.Tool).Concat(content.Recipes.Recipes.SelectMany(Needs)).Distinct();
        return needed
            .Where(item => !got.Contains(item))
            .Select(item => content.Items.Find(item)?.Name ?? item.ToString(CultureInfo.InvariantCulture))
            .Select(name => $"Nothing gives {name}. No shop sells it, no creature drops it, no quest gives it "
                + "and no node or recipe that can be used makes it.");
    }

    /// <summary>A definition that is never put anywhere is data nobody can reach, unless a recipe lights it.</summary>
    private static IEnumerable<string> ObjectsStand(ObjectSet objects, RecipeSet recipes, WorldPlan world)
    {
        HashSet<int> stands =
        [
            .. world.Objects.Select(p => p.ObjectId),
            .. recipes.Recipes.Select(r => r.Product).OfType<Lights>().Select(l => l.ObjectId),
        ];
        return objects.Objects
            .Where(o => !stands.Contains(o.Id))
            .Select(o => $"{o.Name} is never placed and no recipe lights it.");
    }

    private static IEnumerable<string> NpcsStand(NpcSet npcs, WorldPlan world)
    {
        var stands = world.Npcs.Select(s => s.Npc).ToHashSet();
        return npcs.Npcs.Where(n => !stands.Contains(n.Id)).Select(n => $"{n.Name} is never spawned.");
    }

    private static bool Barred(TileMap map, TileCoord tile, TileFlags flags) => map.FlagsAt(tile).Has(flags);

    private static bool SolidAt(IReadOnlyDictionary<TileCoord, ObjectDef> taken, TileCoord tile) =>
        taken.TryGetValue(tile, out var def) && def.Solid;

    /// <summary>The tiles an object covers, or null when it runs off the world.</summary>
    private static List<TileCoord>? Tiles(ObjectDef def, TileCoord at)
    {
        try
        {
            return [.. def.FootprintAt(at).Tiles];
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the server refuses, found before it starts: a tile that is blocked,
    /// water or taken, and stairs that lead nowhere.
    /// </summary>
    private static IEnumerable<string> Ground(ObjectSet objects, NpcSet npcs, WorldPlan world)
    {
        var taken = new Dictionary<TileCoord, ObjectDef>();
        foreach (var placement in world.Objects)
        {
            var def = objects.Find(placement.ObjectId);
            foreach (var line in Placed(world.Map, def, placement, taken))
            {
                yield return line;
            }

            if (def is not null)
            {
                foreach (var tile in Tiles(def, placement.Tile) ?? [])
                {
                    taken[tile] = def;
                }
            }
        }

        foreach (var line in SpawnLands(world, taken).Concat(StairsLand(objects, world, taken)).Concat(CreaturesOn(world, npcs, taken)))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Placed(
        TileMap map,
        ObjectDef? def,
        ObjectPlacement placement,
        IReadOnlyDictionary<TileCoord, ObjectDef> taken) =>
        def is null
            ? [FormattableString.Invariant($"An object {placement.ObjectId} at {Where(placement.Tile)} does not exist.")]
            : [.. Footprint(map, def, placement.Tile, taken), .. Stairs(def, placement)];

    // A footprint never repeats a tile, so checking it all against the tiles
    // taken before it finds what checking tile by tile would.
    private static IEnumerable<string> Footprint(TileMap map, ObjectDef def, TileCoord at, IReadOnlyDictionary<TileCoord, ObjectDef> taken)
    {
        var tiles = Tiles(def, at);
        if (tiles is null)
        {
            yield return $"{def.Name} at {Where(at)} runs off the world.";
            yield break;
        }

        foreach (var tile in tiles)
        {
            if (Barred(map, tile, TileFlags.Blocked | TileFlags.Water))
            {
                yield return $"{def.Name} at {Where(tile)} stands on blocked ground.";
            }

            if (taken.TryGetValue(tile, out var other))
            {
                yield return $"{def.Name} at {Where(tile)} overlaps {other.Name}.";
            }
        }
    }

    private static IEnumerable<string> Stairs(ObjectDef def, ObjectPlacement placement) =>
        def.Options.Any(o => o.Action == "climb") && placement.Destination is null
            ? [$"{def.Name} at {Where(placement.Tile)} leads nowhere."]
            : [];

    /// <summary>Where a new player appears.</summary>
    private static IEnumerable<string> SpawnLands(WorldPlan world, IReadOnlyDictionary<TileCoord, ObjectDef> taken)
    {
        if (Barred(world.Map, world.Spawn, TileFlags.Blocked | TileFlags.Water))
        {
            return [$"The spawn tile {Where(world.Spawn)} is blocked."];
        }

        return SolidAt(taken, world.Spawn) ? [$"The spawn tile {Where(world.Spawn)} is inside a solid object."] : [];
    }

    /// <summary>Where stairs set a player down.</summary>
    private static IEnumerable<string> StairsLand(ObjectSet objects, WorldPlan world, IReadOnlyDictionary<TileCoord, ObjectDef> taken)
    {
        foreach (var placement in world.Objects)
        {
            if (placement.Destination is not { } to)
            {
                continue;
            }

            var from = $"{objects.Find(placement.ObjectId)?.Name ?? "Stairs"} at {Where(placement.Tile)} leads";
            if (Barred(world.Map, to, TileFlags.Blocked | TileFlags.Water))
            {
                yield return $"{from} to blocked ground at {Where(to)}.";
            }
            else if (SolidAt(taken, to))
            {
                yield return $"{from} into a solid object at {Where(to)}.";
            }
        }
    }

    private static IEnumerable<string> CreaturesOn(WorldPlan world, NpcSet npcs, IReadOnlyDictionary<TileCoord, ObjectDef> taken)
    {
        var barred = TileFlags.Blocked | TileFlags.Water | TileFlags.NoNpc;
        foreach (var spawn in world.Npcs)
        {
            var def = npcs.Find(spawn.Npc);
            var who = (def?.Name ?? FormattableString.Invariant($"Creature {spawn.Npc}")) + $" at {Where(spawn.Tile)}";
            if (def is null)
            {
                yield return $"{who} does not exist.";
            }
            else if (Barred(world.Map, spawn.Tile, barred))
            {
                yield return $"{who} stands on ground it cannot use.";
            }
            else if (SolidAt(taken, spawn.Tile))
            {
                yield return $"{who} stands inside a solid object.";
            }
        }
    }

    /// <summary>
    /// Everything must be reachable on foot from the spawn tile. A door counts as
    /// open, because a player can open it, and a stair joins its two floors. A
    /// player stands beside a thing and not on it, across no wall.
    /// </summary>
    private static IEnumerable<string> Reach(ObjectSet objects, WorldPlan world)
    {
        var reached = Walkable(objects, world);
        foreach (var placement in world.Objects)
        {
            if (objects.Find(placement.ObjectId) is { } def
                && Tiles(def, placement.Tile) is { } tiles
                && !Near(world.Map, reached, tiles))
            {
                yield return $"{def.Name} at {Where(placement.Tile)} cannot be reached from the spawn tile.";
            }
        }

        foreach (var spawn in world.Npcs.Where(s => !Near(world.Map, reached, [s.Tile])))
        {
            yield return $"A creature at {Where(spawn.Tile)} cannot be reached from the spawn tile.";
        }
    }

    /// <summary>Whether a player can stand on one of <paramref name="tiles"/>, or step onto it from beside it.</summary>
    private static bool Near(TileMap map, HashSet<TileCoord> reached, IEnumerable<TileCoord> tiles) =>
        tiles.Any(tile => reached.Contains(tile) || Step(tile, Straight).Any(from => reached.Contains(from) && map.CanStep(from, tile)));

    /// <summary>The tiles <paramref name="tile"/> touches at <paramref name="offsets"/> that are in the world.</summary>
    private static IEnumerable<TileCoord> Step(TileCoord tile, (int Dx, int Dy)[] offsets)
    {
        foreach (var (dx, dy) in offsets)
        {
            var x = tile.X + dx;
            var y = tile.Y + dy;
            if (x >= 0 && y >= 0 && x < TileWorld.WorldSize && y < TileWorld.WorldSize)
            {
                yield return new TileCoord(x, y, tile.Plane);
            }
        }
    }

    /// <summary>
    /// The tiles a player can walk to from the spawn tile. A solid object that
    /// cannot be opened stands in the way.
    /// </summary>
    private static HashSet<TileCoord> Walkable(ObjectSet objects, WorldPlan world)
    {
        var links = new Dictionary<TileCoord, TileCoord>();
        foreach (var placement in world.Objects)
        {
            if (placement.Destination is { } to)
            {
                links[placement.Tile] = to;
            }
        }

        var shut = Shut(objects, world);
        var seen = new HashSet<TileCoord> { world.Spawn };
        var queue = new Queue<TileCoord>([world.Spawn]);
        while (queue.TryDequeue(out var tile))
        {
            if (links.TryGetValue(tile, out var climb) && seen.Add(climb))
            {
                queue.Enqueue(climb);
            }

            foreach (var to in Step(tile, Around))
            {
                if (!shut.Contains(to) && world.Map.CanStep(tile, to) && seen.Add(to))
                {
                    queue.Enqueue(to);
                }
            }
        }

        return seen;
    }

    /// <summary>The tiles of solid objects with nothing to open or close.</summary>
    private static HashSet<TileCoord> Shut(ObjectSet objects, WorldPlan world)
    {
        var shut = new HashSet<TileCoord>();
        foreach (var placement in world.Objects)
        {
            if (objects.Find(placement.ObjectId) is { Solid: true } def && !def.Options.Any(o => o.Action == "toggle"))
            {
                shut.UnionWith(Tiles(def, placement.Tile) ?? []);
            }
        }

        return shut;
    }
}
