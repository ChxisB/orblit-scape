using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Scape.Core;

namespace Scape.Server;

/// <summary>What a player may do with the commands. A command names the lowest role that may run it.</summary>
public enum Role
{
    Player,
    Moderator,
    Admin,
}

/// <summary>What the commands work on.</summary>
internal sealed record AdminOptions
{
    /// <summary>The roles by name key. Anyone not named holds <see cref="Role.Player"/>.</summary>
    public required IReadOnlyDictionary<string, Role> Roles { get; init; }

    public required Ledger Ledger { get; init; }

    public required Social Social { get; init; }

    public required Movement Movement { get; init; }

    public required Skills Skills { get; init; }

    public required SkillSet SkillSet { get; init; }

    public required Items Bags { get; init; }

    public required ItemSet Items { get; init; }

    /// <summary>Rolls a player back some minutes and says what happened. Null for a server whose store keeps no history.</summary>
    public Func<string, int, Task<string>>? Rollback { get; init; }

    /// <summary>Ends the server when a countdown runs out.</summary>
    public required Action Stop { get; init; }

    public required Action<string> Log { get; init; }
}

/// <summary>
/// Commands typed as chat lines that start with <see cref="Wire.CommandMarker"/>. The server reads
/// them, never the client, and a player who holds no role is told the command does not exist.
/// </summary>
/// <remarks>
/// Mutes and bans are kept in the <see cref="Ledger"/>, which saves them, so they hold across a
/// restart. A mute is applied to chat each tick from the ledger, so it ends by itself.
/// </remarks>
internal sealed partial class Admin
{
    private const string NoReason = "no reason given";

    /// <summary>The largest number a command takes, so a minute count cannot overflow.</summary>
    private const int MaxNumber = 1 << 30;

    /// <summary>Seconds left at which a shutdown countdown tells everyone.</summary>
    private static readonly long[] WarnAt = [60, 30, 10, 5, 4, 3, 2, 1];

    private readonly Registry _registry;
    private readonly Services _services;
    private readonly AdminOptions _options;
    private readonly Dictionary<string, Command> _commands;
    private readonly HashSet<string> _players = [];

    /// <summary>The tick a shutdown happens on, or null when none is set.</summary>
    private long? _stopAt;

    /// <summary>The seconds a countdown last told everyone, so it says each once.</summary>
    private long _warned;

    private Admin(Registry registry, Services services, AdminOptions options)
    {
        _registry = registry;
        _services = services;
        _options = options;
        _commands = new Dictionary<string, Command>
        {
            ["kick"] = new(Role.Moderator, "/kick <name> [reason]", c => Task.FromResult(Kick(c))),
            ["mute"] = new(Role.Moderator, "/mute <name> <minutes, 0 for no end> [reason]", c => Task.FromResult(Impose(SanctionKind.Mute, c))),
            ["unmute"] = new(Role.Moderator, "/unmute <name>", c => Task.FromResult(Lift(SanctionKind.Mute, c))),
            ["ban"] = new(Role.Moderator, "/ban <name> <minutes, 0 for no end> [reason]", c => Task.FromResult(Impose(SanctionKind.Ban, c))),
            ["unban"] = new(Role.Moderator, "/unban <name>", c => Task.FromResult(Lift(SanctionKind.Ban, c))),
            ["teleport"] = new(Role.Admin, "/teleport <name> <x> <y> [plane]", c => Task.FromResult(Teleport(c))),
            ["give"] = new(Role.Admin, "/give <name> <item id> [count]", c => Task.FromResult(Give(c))),
            ["skill"] = new(Role.Admin, "/skill <name> <skill id> <experience>", c => Task.FromResult(SetSkill(c))),
            ["broadcast"] = new(Role.Admin, "/broadcast <text>", c => Task.FromResult(Broadcast(c))),
            ["shutdown"] = new(Role.Admin, "/shutdown <seconds>", c => Task.FromResult(Shutdown(c))),
            ["cancelshutdown"] = new(Role.Admin, "/cancelshutdown", _ => Task.FromResult(CancelShutdown())),
            ["rollback"] = new(Role.Admin, "/rollback <name> <minutes>", RollbackPlayerAsync),
        };
    }

    /// <summary>Adds the commands to <paramref name="registry"/>.</summary>
    public static void Register(Registry registry, Services services, AdminOptions options)
    {
        var admin = new Admin(registry, services, options);
        options.Social.Reserve(Wire.CommandMarker);
        registry.AddJoinHandler(player => admin._players.Add(player));
        registry.AddGoneHandler(player => admin._players.Remove(player));
        registry.AddIntentHandler(admin.OnIntent);
        registry.AddSystem(Phase.Outbound, admin.Tick);
    }

    // The spaces between words, listed, because \s also takes U+0085 and leaves out U+FEFF.
    [GeneratedRegex("\"([^\"]*)\"|([^\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff]+)")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"[\x00-\x1f\x7f]")]
    private static partial Regex ControlPattern();

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Command names are lower case.")]
    private static string CommandName(List<string> words) => words.Count > 0 ? words[0].ToLowerInvariant() : "";

    private static string Target(Call call)
    {
        var word = call.Word();
        var key = Wire.NameKey(word);
        return Wire.IsValidName(key) ? key : throw new UsageException($"{word} is not a valid name.");
    }

    private static string DoneOf(SanctionKind kind) => kind switch
    {
        SanctionKind.Mute => "muted",
        SanctionKind.Ban => "banned",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Seconds(long count) => count == 1 ? "1 second" : FormattableString.Invariant($"{count} seconds");

    /// <summary>
    /// <paramref name="text"/> as a notice may carry it: no control characters, and no more than
    /// <see cref="Wire.MaxTextBytes"/> bytes.
    /// </summary>
    private static string Fit(string text)
    {
        var bytes = 0;
        var end = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > Wire.MaxTextBytes)
            {
                break;
            }

            end += rune.Utf16SequenceLength;
        }

        // A control character and a space are one byte each, so the cut stays where it is.
        return ControlPattern().Replace(text[..end], " ");
    }

    private void OnIntent(string player, Intent intent)
    {
        if (intent is Chat chat && chat.Text.StartsWith(Wire.CommandMarker, StringComparison.Ordinal))
        {
            _ = RunAsync(player, chat.Text[Wire.CommandMarker.Length..]);
        }
    }

    private async Task RunAsync(string by, string line)
    {
        var words = WordPattern().Matches(line).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();
        var name = CommandName(words);
        if (!_commands.TryGetValue(name, out var command) || RoleOf(by) < command.Role)
        {
            _options.Log($"Admin: {by} was refused \"{name}\"");
            Tell(by, "Unknown command.");
            return;
        }

        try
        {
            var done = await command.Run(new Call(by, words.Skip(1), command.Usage));
            _options.Log($"Admin: {by} {name} {done.Whom}: {done.Why}");
            Tell(by, done.Told);
        }
        catch (UsageException usage)
        {
            _options.Log($"Admin: {by} {name} failed: {usage.Message}");
            Tell(by, usage.Message);
        }
#pragma warning disable CA1031 // A command must never take the server down with it.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _options.Log($"Admin: {by} {name} crashed: {e.Message}");
            Tell(by, "That command failed.");
        }
    }

    private Role RoleOf(string player) => _options.Roles.GetValueOrDefault(player, Role.Player);

    /// <summary>
    /// Throws unless <paramref name="by"/> holds a higher role than <paramref name="target"/>, so a
    /// moderator cannot silence an admin or another moderator.
    /// </summary>
    private void MayAct(string by, string target)
    {
        if (RoleOf(by) <= RoleOf(target))
        {
            throw new UsageException($"You cannot do that to {target}.");
        }
    }

    private Done Kick(Call call)
    {
        var target = Online(call.Word());
        MayAct(call.By, target);
        var why = call.Rest(NoReason);
        Tell(target, $"You were kicked: {why}");
        _ = KickOutAsync(target);
        return new Done(target, why, $"{target} was kicked.");
    }

    private Done Impose(SanctionKind kind, Call call)
    {
        var target = Target(call);
        MayAct(call.By, target);
        var minutes = call.Number();
        var why = call.Rest(NoReason);
        _options.Ledger.Add(kind, target, new SanctionOrder(call.By, why, minutes == 0 ? null : TimeSpan.FromMinutes(minutes)));
        if (_players.Contains(target))
        {
            Tell(target, $"You are {DoneOf(kind)}: {why}");
            if (kind == SanctionKind.Ban)
            {
                _ = KickOutAsync(target);
            }
            else
            {
                SyncMute(target);
            }
        }

        var span = minutes == 0 ? "with no end" : FormattableString.Invariant($"for {minutes} minutes");
        return new Done(target, $"{why}, {span}", $"{target} is {DoneOf(kind)} {span}.");
    }

    private Done Lift(SanctionKind kind, Call call)
    {
        var target = Target(call);
        if (target == call.By)
        {
            throw new UsageException("You cannot lift a sanction on yourself.");
        }

        if (_options.Ledger.Active(kind, target) is null)
        {
            throw new UsageException($"{target} is not {DoneOf(kind)}.");
        }

        _options.Ledger.Lift(kind, target);
        return new Done(target, "lifted", $"{target} is no longer {DoneOf(kind)}.");
    }

    private Done Teleport(Call call)
    {
        var target = Online(call.Word());
        var x = call.Number();
        var y = call.Number();
        var plane = call.Number(otherwise: 0);
        _options.Movement.Teleport(target, Standable(x, y, plane));
        return new Done(target, FormattableString.Invariant($"to {x} {y} {plane}"), FormattableString.Invariant($"{target} is at {x}, {y}."));
    }

    private TileCoord Standable(int x, int y, int plane)
    {
        TileCoord tile;
        try
        {
            tile = new TileCoord(x, y, plane);
        }
        catch (ArgumentException)
        {
            throw new UsageException("That tile is outside the world.");
        }

        return _services.Map.FlagsAt(tile).Has(TileFlags.Blocked | TileFlags.Water)
            ? throw new UsageException("Nobody can stand there.")
            : tile;
    }

    private Done Give(Call call)
    {
        var target = Online(call.Word());
        var id = call.Number(min: 1);
        var count = call.Number(min: 1, otherwise: 1);
        var def = _options.Items.Find(id) ?? throw new UsageException("There is no such item.");
        var bag = _options.Bags.ContainerOf(target, ContainerId.Inventory) ?? throw new UsageException($"{target} has no inventory yet.");
        var added = bag.Add(id, count);
        Tell(target, FormattableString.Invariant($"You were given {added} x {def.Name}."));
        return new Done(
            target,
            FormattableString.Invariant($"{added} of {count} x {def.Name}"),
            FormattableString.Invariant($"{target} took {added} of {count} x {def.Name}."));
    }

    private Done SetSkill(Call call)
    {
        var target = Online(call.Word());
        var id = call.Word();
        var xp = call.Number();
        if (_options.SkillSet.Find(id) is null)
        {
            throw new UsageException("There is no such skill.");
        }

        _options.Skills.Restore(target, new Dictionary<string, long> { [id] = xp });
        return new Done(target, FormattableString.Invariant($"{id} to {xp}"), FormattableString.Invariant($"{target} has {xp} in {id}."));
    }

    private Done Broadcast(Call call)
    {
        var text = call.Rest();
        Announce(text);
        return new Done("everyone", text, "Sent.");
    }

    private Done Shutdown(Call call)
    {
        var seconds = call.Number(min: 1);
        var ticks = (long)Math.Ceiling(seconds * 1000 / GameTick.Period.TotalMilliseconds);
        _stopAt = _registry.Actions.Tick + ticks;
        _warned = seconds;
        Announce($"The server shuts down in {Seconds(seconds)}.");
        return new Done("everyone", $"in {Seconds(seconds)}", "Set.");
    }

    private Done CancelShutdown()
    {
        if (_stopAt is null)
        {
            throw new UsageException("No shutdown is set.");
        }

        _stopAt = null;
        Announce("The shutdown was cancelled.");
        return new Done("everyone", "cancelled", "Cancelled.");
    }

    private async Task<Done> RollbackPlayerAsync(Call call)
    {
        var target = Target(call);
        var minutes = call.Number(min: 1);
        var rollback = _options.Rollback ?? throw new UsageException("This server keeps no save history.");
        if (_players.Contains(target))
        {
            Tell(target, "You are logged out while your save is rolled back.");
        }

        var told = await rollback(target, minutes);
        return new Done(target, FormattableString.Invariant($"{minutes} minutes"), told);
    }

    private async Task KickOutAsync(string player)
    {
        try
        {
            await _services.Hosting.Kick(player);
        }
#pragma warning disable CA1031 // Nobody waits on the kick, so a failure is logged rather than lost.
        catch (Exception e)
#pragma warning restore CA1031
        {
            _options.Log($"Admin: kicking {player} failed: {e.Message}");
        }
    }

    private void Tick()
    {
        foreach (var player in _players)
        {
            SyncMute(player);
        }

        Countdown();
    }

    private void Countdown()
    {
        if (_stopAt is not { } at)
        {
            return;
        }

        var left = at - _registry.Actions.Tick;
        var seconds = (long)Math.Ceiling(left * GameTick.Period.TotalMilliseconds / 1000);
        if (seconds <= 0)
        {
            _stopAt = null;
            _options.Stop();
        }
        else if (WarnAt.Contains(seconds) && seconds != _warned)
        {
            _warned = seconds;
            Announce($"The server shuts down in {Seconds(seconds)}.");
        }
    }

    /// <summary>Makes chat agree with the ledger for <paramref name="player"/>: muted while a mute has not ended, free after.</summary>
    private void SyncMute(string player)
    {
        var muted = _options.Ledger.Active(SanctionKind.Mute, player) is not null;
        if (muted == _options.Social.IsMuted(player))
        {
            return;
        }

        if (muted)
        {
            _options.Social.Mute(player);
        }
        else
        {
            _options.Social.Unmute(player);
            Tell(player, "You can chat again.");
        }
    }

    private string Online(string name)
    {
        var key = Wire.NameKey(name);
        return _players.Contains(key) ? key : throw new UsageException($"{name} is not online.");
    }

    private void Announce(string text)
    {
        foreach (var player in _players)
        {
            Tell(player, $"[Server] {text}");
        }
    }

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(Fit(text)));

    /// <summary>A command that was written wrong, or asked for what cannot be done.</summary>
    private sealed class UsageException(string message) : Exception(message);

    /// <summary>
    /// The words of one command line, read in order. A word in double quotes may hold spaces, so a
    /// name with a space can be named.
    /// </summary>
    // F5: a reader hands out each word once, so taking a word moves past it.
    private sealed class Call(string by, IEnumerable<string> words, string usage)
    {
        private readonly List<string> _words = [.. words];
        private int _next;

        public string By => by;

        public string Word() => _next < _words.Count ? _words[_next++] : throw Usage();

        public int Number(int min = 0, int? otherwise = null)
        {
            if (_next >= _words.Count && otherwise is { } fallback)
            {
                return fallback;
            }

            // NumberStyles.None takes plain ASCII digits, with no sign, space or hex.
            return int.TryParse(Word(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= MaxNumber
                ? value
                : throw Usage();
        }

        /// <summary>What is left, joined, or <paramref name="otherwise"/> when nothing is.</summary>
        public string Rest(string? otherwise = null)
        {
            var text = string.Join(' ', _words.Skip(_next));
            _next = _words.Count;
            return text.Length > 0 ? text : otherwise ?? throw Usage();
        }

        private UsageException Usage() => new($"Use {usage}");
    }

    /// <summary>
    /// Whom a command was for, what it did and what the caller is told. The log line is made from
    /// it, so a command cannot forget to write one.
    /// </summary>
    private sealed record Done(string Whom, string Why, string Told);

    private sealed record Command(Role Role, string Usage, Func<Call, Task<Done>> Run);
}
