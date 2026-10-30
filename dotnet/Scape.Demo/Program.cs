using Avalonia;
using Scape.Core;

namespace Scape.Demo;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        AppBuilder.Configure(() => new DemoApp(() => new IslandWindow(Start(args))))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);

    // "--walk X Y" sends the player to a tile as the window opens, so a walk can be seen without a click.
    private static Island Start(string[] args)
    {
        var island = new Island(new Random());
        var at = Array.IndexOf(args, "--walk");
        if (at >= 0 && at + 2 < args.Length && int.TryParse(args[at + 1], out var x) && int.TryParse(args[at + 2], out var y))
        {
            island.WalkTo(new TileCoord(x, y, 0), MoveSpeed.Walk);
        }

        return island;
    }
}
