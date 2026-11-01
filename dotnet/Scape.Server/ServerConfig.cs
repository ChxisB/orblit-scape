using System.Globalization;
using System.Text.RegularExpressions;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// How much the server binary writes to its log. <see cref="Warn"/> keeps what went wrong.
/// <see cref="Info"/> adds the start line and the health line each minute.
/// </summary>
public enum LogLevel
{
    Warn,
    Info,
}

/// <summary>
/// What the server binary reads from its environment. Each value has a default. A value that does
/// not parse throws a <see cref="FormatException"/> that names the variable, so a typo stops the
/// server before it listens. An empty value counts as unset. Only <see cref="FromEnvironment"/>
/// builds one, so every instance has passed the checks.
/// </summary>
public sealed partial class ServerConfig
{
    private const int TopPort = 65535;

    private ServerConfig()
    {
    }

    /// <summary>The port players connect to.</summary>
    public int Port { get; private init; }

    public string Address { get; private init; } = "";

    /// <summary>The folder that holds every world's saves.</summary>
    public string Data { get; private init; } = "";

    /// <summary>The name of this world. Its saves sit in a folder of that name in <see cref="Data"/>.</summary>
    public string World { get; private init; } = "";

    /// <summary>The port that answers <c>alive</c>, or 0 for none.</summary>
    public int HealthPort { get; private init; }

    public LogLevel LogLevel { get; private init; }

    /// <summary>Who may run commands, from <c>SCAPE_ADMINS</c>, for example <c>ana=admin,bo=moderator</c>.</summary>
    public IReadOnlyDictionary<string, Role> Roles { get; private init; } = new Dictionary<string, Role>();

    public string Saves => $"{Data}/{World}";

    public static ServerConfig FromEnvironment(IReadOnlyDictionary<string, string> env)
    {
        string? Value(string name) => env.GetValueOrDefault(name) is { Length: > 0 } text ? text : null;

        var port = PortOf(Value("SCAPE_PORT"), "SCAPE_PORT", 7600, least: 1);
        var healthPort = PortOf(Value("SCAPE_HEALTH_PORT"), "SCAPE_HEALTH_PORT", 7601, least: 0);
        if (healthPort == port)
        {
            throw new FormatException(FormattableString.Invariant($"SCAPE_HEALTH_PORT and SCAPE_PORT are both {port}. They must differ"));
        }

        return new ServerConfig
        {
            Port = port,
            Address = Value("SCAPE_ADDRESS") ?? "127.0.0.1",
            Data = Value("SCAPE_DATA") ?? "scape-saves",
            World = WorldOf(Value("SCAPE_WORLD")),
            HealthPort = healthPort,
            LogLevel = LevelOf(Value("SCAPE_LOG_LEVEL")),
            Roles = RolesOf(Value("SCAPE_ADMINS")),
        };
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,31}\z")]
    private static partial Regex WorldName();

    private static int PortOf(string? text, string name, int fallback, int least)
    {
        if (text is null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port >= least && port <= TopPort
            ? port
            : throw new FormatException(FormattableString.Invariant($"{name} wants a whole number from {least} to {TopPort}, not \"{text}\""));
    }

    private static string WorldOf(string? text) =>
        text is null ? "scape"
        : WorldName().IsMatch(text) ? text
        : throw new FormatException($"SCAPE_WORLD wants 1 to 32 lower-case letters, digits, \"-\" or \"_\", not \"{text}\"");

    private static LogLevel LevelOf(string? text) => text switch
    {
        null or "info" => LogLevel.Info,
        "warn" => LogLevel.Warn,
        _ => throw new FormatException($"SCAPE_LOG_LEVEL wants warn or info, not \"{text}\""),
    };

    private static Role? RoleNamed(string text) => text switch
    {
        "player" => Role.Player,
        "moderator" => Role.Moderator,
        "admin" => Role.Admin,
        _ => null,
    };

    private static Dictionary<string, Role> RolesOf(string? text)
    {
        var roles = new Dictionary<string, Role>();
        foreach (var pair in (text ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var parts = pair.Split('=').Select(p => p.Trim()).ToArray();
            if (parts.Length != 2 || RoleNamed(parts[1]) is not { } role || !Wire.IsValidName(parts[0]))
            {
                throw new FormatException($"SCAPE_ADMINS has \"{pair}\"");
            }

            // A player is known by the lower-case key, so a role under any other spelling would
            // never apply.
            if (!roles.TryAdd(Wire.NameKey(parts[0]), role))
            {
                throw new FormatException($"SCAPE_ADMINS names {parts[0]} twice");
            }
        }

        return roles;
    }
}
