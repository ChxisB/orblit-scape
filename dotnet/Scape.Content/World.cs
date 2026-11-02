using Scape.Core;

namespace Scape.Content;

/// <summary>
/// The ground and everything standing on it: the flags of every region, where
/// a new player appears, the objects and the creatures.
/// </summary>
public sealed class WorldPlan(TileMap map, TileCoord spawn, IReadOnlyList<ObjectPlacement> objects, IReadOnlyList<NpcSpawn> npcs)
{
    /// <summary>Mutable, because the server flags the tiles of a solid object blocked.</summary>
    public TileMap Map { get; } = map;

    public TileCoord Spawn { get; } = spawn;

    public IReadOnlyList<ObjectPlacement> Objects { get; } = [.. objects];

    public IReadOnlyList<NpcSpawn> Npcs { get; } = [.. npcs];
}
