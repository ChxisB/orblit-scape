using System.Text;
using Orblit.Json;
using Orblit.Save;
using Scape.Core;

namespace Scape.Server;

internal enum SanctionKind
{
    Mute,
    Ban,
}

/// <summary>Why a moderator held a player back, and until when.</summary>
/// <param name="Until">
/// When it ends, in milliseconds since 1970 UTC, or null when it has no end. A ledger may hold a
/// time past the year 9999, so it stays a count.
/// </param>
internal sealed record Sanction(string By, string Reason, long? Until)
{
    public bool IsActive(DateTimeOffset now) => Until is not { } end || now.ToUnixTimeMilliseconds() < end;
}

/// <summary>What a moderator puts on a player: who gave it, why, and how long it lasts, or null for no end.</summary>
internal sealed record SanctionOrder(string By, string Reason, TimeSpan? Lasting);

/// <summary>
/// Who is muted or banned, and who is locked out for a moment. Mutes and bans are one record in
/// the save store, so they outlast a restart. Locks are kept in memory only.
/// </summary>
/// <remarks>
/// <see cref="LoadAsync"/> must finish before the first read, because a restart starts with
/// nothing. A login does that on its way in, so no one is online before it. The tick, the logins
/// and a rollback each reach the ledger from their own thread, so it keeps a lock.
/// </remarks>
internal sealed class Ledger(SaveQueue queue, Func<DateTimeOffset> now)
{
    private const long Format = 1;

    private const string Damaged = "The sanction ledger is damaged.";

    /// <summary>The farthest a time may be from 1970 either way, 100,000,000 days, in milliseconds.</summary>
    private const long MaxMillis = 8_640_000_000_000_000;

    private readonly Lock _gate = new();
    private readonly OrderedDictionary<string, Sanction> _mutes = [];
    private readonly OrderedDictionary<string, Sanction> _bans = [];
    private readonly HashSet<string> _locked = [];
    private Task? _loaded;
    private bool _ready;

    /// <summary>
    /// Reads the record once. Throws <see cref="FormatException"/> when it is damaged, so a login is
    /// refused and the record is never written over. A later call tries again after a failure.
    /// </summary>
    public async Task LoadAsync()
    {
        Task run;
        lock (_gate)
        {
            run = _loaded ??= ReadAsync();
        }

        try
        {
            await run;
        }
        catch
        {
            lock (_gate)
            {
                _loaded = null;
            }

            throw;
        }
    }

    /// <summary>The sanction on <paramref name="player"/> that has not ended, or null.</summary>
    public Sanction? Active(SanctionKind kind, string player)
    {
        lock (_gate)
        {
            return Of(kind).GetValueOrDefault(player) is { } found && found.IsActive(now()) ? found : null;
        }
    }

    /// <summary>
    /// Puts a sanction on <paramref name="player"/> and saves. Throws
    /// <see cref="InvalidOperationException"/> before <see cref="LoadAsync"/> has finished, since the
    /// save would drop what the store holds.
    /// </summary>
    public void Add(SanctionKind kind, string player, SanctionOrder order)
    {
        lock (_gate)
        {
            CheckReady();
            var at = now();
            long? until = order.Lasting is { } lasting ? at.ToUnixTimeMilliseconds() + (long)lasting.TotalMilliseconds : null;
            Of(kind)[player] = new Sanction(order.By, order.Reason, until);
            Save(at);
        }
    }

    public void Lift(SanctionKind kind, string player)
    {
        lock (_gate)
        {
            CheckReady();
            Of(kind).Remove(player);
            Save(now());
        }
    }

    /// <summary>Keeps <paramref name="player"/> from logging in until <see cref="Unlock"/>.</summary>
    public void Lock(string player)
    {
        lock (_gate)
        {
            _locked.Add(player);
        }
    }

    public void Unlock(string player)
    {
        lock (_gate)
        {
            _locked.Remove(player);
        }
    }

    public bool IsLocked(string player)
    {
        lock (_gate)
        {
            return _locked.Contains(player);
        }
    }

    private static OrderedDictionary<string, object?> Part(OrderedDictionary<string, Sanction> held, DateTimeOffset at)
    {
        var part = new OrderedDictionary<string, object?>();
        foreach (var (player, sanction) in held)
        {
            if (sanction.IsActive(at))
            {
                part[player] = new OrderedDictionary<string, object?>
                {
                    ["by"] = sanction.By,
                    ["reason"] = sanction.Reason,
                    ["until"] = sanction.Until,
                };
            }
        }

        return part;
    }

    /// <summary>The sanctions in one part of a record, or null when it is damaged.</summary>
    private static OrderedDictionary<string, Sanction>? ReadPart(object? json)
    {
        if (json is not OrderedDictionary<string, object?> entries)
        {
            return null;
        }

        var part = new OrderedDictionary<string, Sanction>();
        foreach (var (player, value) in entries)
        {
            if (ReadSanction(value) is not { } sanction)
            {
                return null;
            }

            part[player] = sanction;
        }

        return part;
    }

    // An end must be written out, as null when there is none, so a record cut short is not read as
    // a sanction with no end.
    private static Sanction? ReadSanction(object? json) =>
        json is OrderedDictionary<string, object?> entry
        && entry.GetValueOrDefault("by") is string by
        && entry.GetValueOrDefault("reason") is string reason
        && entry.TryGetValue("until", out var until)
        && until is null or long and >= -MaxMillis and <= MaxMillis
            ? new Sanction(by, reason, (long?)until)
            : null;

    private async Task ReadAsync()
    {
        var record = await queue.ReadAsync(StoreKeys.Ledger);
        lock (_gate)
        {
            if (record is not null)
            {
                Decode(record.Bytes.Span);
            }

            _ready = true;
        }
    }

    private OrderedDictionary<string, Sanction> Of(SanctionKind kind) => kind switch
    {
        SanctionKind.Mute => _mutes,
        SanctionKind.Ban => _bans,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private void CheckReady()
    {
        if (!_ready)
        {
            throw new InvalidOperationException("The ledger was changed before it loaded.");
        }
    }

    // Called with the lock held, so the records reach the queue in the order they changed.
    private void Save(DateTimeOffset at) =>
        queue.Save(StoreKeys.Ledger, Encoding.UTF8.GetBytes(JsonText.Encode(new OrderedDictionary<string, object?>
        {
            ["v"] = Format,
            ["mute"] = Part(_mutes, at),
            ["ban"] = Part(_bans, at),
        })), new Custom("sanction"));

    private void Decode(ReadOnlySpan<byte> bytes)
    {
        object? json;
        try
        {
            json = JsonText.Decode(Wire.DecodeText(bytes));
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new FormatException(Damaged, e);
        }

        // A version written as 1.0 is the same number.
        if (json is not OrderedDictionary<string, object?> record
            || record.GetValueOrDefault("v") is not (Format or 1.0)
            || ReadPart(record.GetValueOrDefault("mute")) is not { } mutes
            || ReadPart(record.GetValueOrDefault("ban")) is not { } bans)
        {
            throw new FormatException(Damaged);
        }

        foreach (var (player, sanction) in mutes)
        {
            _mutes[player] = sanction;
        }

        foreach (var (player, sanction) in bans)
        {
            _bans[player] = sanction;
        }
    }
}
