using Scape.Client;
using Scape.Core;

namespace Scape.Bots;

/// <summary>What a <see cref="Bot"/> plays with.</summary>
public sealed record BotOptions
{
    public required string Name { get; init; }

    public required GameSession Session { get; init; }

    public required TileCoord Home { get; init; }

    /// <summary>How many tiles from <see cref="Home"/> a walk may end.</summary>
    public required int Reach { get; init; }

    public required Random Random { get; init; }
}

/// <summary>
/// A client program. It logs in over the wire and picks what to do from what the server told it,
/// as a player's client would. It walks to tiles near its home without looking at the map, so some
/// walks end in walls or on tiles the map does not hold, on purpose.
/// </summary>
public sealed class Bot
{
    private readonly BotOptions _options;
    private int _wait;

    public Bot(BotOptions options)
    {
        _options = options;
        options.Session.Login(options.Name);
    }

    public string Name => _options.Name;

    public GameSession Session => _options.Session;

    public bool IsIn => Session.AvatarId is not null;

    /// <summary>Does one thing now and then. Call once a tick.</summary>
    public void Act()
    {
        if (!IsIn || --_wait > 0)
        {
            return;
        }

        var random = _options.Random;
        _wait = 1 + random.Next(6);

        // Six walks to every two fights and two uses.
        switch (random.Next(10))
        {
            case < 6:
                Walk();
                break;
            case < 8:
                Fight();
                break;
            default:
                Use();
                break;
        }
    }

    private void Walk()
    {
        var (home, reach, random) = (_options.Home, _options.Reach, _options.Random);
        int Near(int at) => Math.Clamp(at + random.Next(reach * 2 + 1) - reach, 0, TileWorld.WorldSize - 1);
        Session.WalkTo(new TileCoord(Near(home.X), Near(home.Y), home.Plane));
    }

    private void Fight()
    {
        var creatures = Session.Npcs;
        if (creatures.Count == 0)
        {
            return;
        }

        Session.Attack(creatures[_options.Random.Next(creatures.Count)].NetworkId);
    }

    private void Use()
    {
        var things = Session.Objects;
        if (things.Count == 0)
        {
            return;
        }

        Session.UseObject(things[_options.Random.Next(things.Count)].NetworkId, 0);
    }
}
