using Scape.Core;

namespace Scape.Server.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FakeTime
{
    public TimeSpan Now { get; private set; }

    public TimeSpan Read() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Never waits, so the test steps the loop by hand.</summary>
internal sealed class NoPacer : IPacer
{
    public Task UntilNextAsync() => Task.CompletedTask;
}

internal static class OpenWorld
{
    public static readonly TileCoord SpawnTile = new(32, 32, 0);

    /// <summary>Open ground, two regions across and two up, on plane 0.</summary>
    public static TileMap Map()
    {
        var map = new TileMap();
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                map.AddRegion(FlagRegion.Open(new RegionKey(x, y, 0)));
            }
        }

        return map;
    }
}
