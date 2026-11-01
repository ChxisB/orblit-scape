namespace Scape.Core;

public static class GameTick
{
    /// <summary>
    /// How long a game tick lasts. The server steps the world this often and a client
    /// blends between the states it is sent over about as long.
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromMilliseconds(600);
}
