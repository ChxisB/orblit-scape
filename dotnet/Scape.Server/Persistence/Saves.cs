using Orblit.Net;
using Orblit.Save;
using Scape.Core;

namespace Scape.Server;

/// <summary>What <see cref="Saves"/> works with.</summary>
internal sealed record SavesOptions
{
    public required SaveQueue Queue { get; init; }

    public required PlayerStates States { get; init; }

    public required AutoSaver AutoSaver { get; init; }

    /// <summary>Whether a player's session is attached, lingering or gone. It takes the host's lock.</summary>
    public required Func<string, SessionState?> SessionOf { get; init; }

    /// <summary>Takes a line about a save that was damaged, cut short or not written. It may be called off the tick.</summary>
    public required Action<string> Log { get; init; }
}

/// <summary>Keeps players between visits.</summary>
/// <remarks>
/// A save is captured on the tick and handed to the <see cref="SaveQueue"/>, which writes it
/// somewhere else, so the tick never waits on a disk. A login reads the save before the player
/// joins, in <see cref="PrepareAsync"/>, because the join itself is one step on the tick and cannot
/// wait. That read goes on on whatever thread the store answers on, so what it shares with the tick
/// is behind a lock of its own. The host's lock is always taken first.
/// </remarks>
internal sealed class Saves(SavesOptions options)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// The state each player who is logging in will join with. A player who is admitted and never
    /// seated leaves theirs here until they log in again.
    /// </summary>
    private readonly Dictionary<string, PlayerState> _pending = [];

    /// <summary>
    /// Players whose login found their session still held, so they will take it back with what the
    /// world has. A session that ends before the host seats them hands its state over in
    /// <see cref="Left"/>, as the host then starts a new one.
    /// </summary>
    private readonly HashSet<string> _resuming = [];

    /// <summary>
    /// How many saves have been queued for each key. A read that began before a save was queued
    /// may have found an older one, so it counts them.
    /// </summary>
    // shortcut: one small entry for each name saved in this run, never pruned. Fine to a few
    // hundred thousand names, then drop the keys of players who have left and have nothing in flight.
    private readonly Dictionary<string, int> _queued = [];

    /// <summary>Journal clears that may still be waiting on the store. A stop waits for them.</summary>
    private readonly List<Task> _clearing = [];

    private Task? _recovered;

    /// <summary>
    /// Reads the save of <paramref name="player"/> so <see cref="Arrived"/> can put it on them.
    /// Throws <see cref="FormatException"/> when the save is damaged, so a login is refused and a
    /// blank is never written over a real save.
    /// </summary>
    /// <remarks>
    /// A save queued while this read was away, such as the one a player makes by leaving, may be
    /// newer than what the read found, so it reads again. Nothing after the last read waits, so no
    /// save can be queued before the host seats the player.
    /// </remarks>
    public async Task PrepareAsync(string player)
    {
        await RecoverAsync();
        var key = StoreKeys.SaveOf(player);
        while (true)
        {
            var before = QueuedOf(key);
            var record = await options.Queue.ReadAsync(key);
            var resuming = options.SessionOf(player) is not null;
            lock (_gate)
            {
                if (before == _queued.GetValueOrDefault(key))
                {
                    Hold(player, record, resuming);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Saves two players whose items changed together, so a crash cannot leave one save with the
    /// items and the other with them as well. The states go down first as a journal. A restart that
    /// finds a journal brings any save older than it up to it, before the first login. The journal
    /// is emptied once both saves are on the store.
    /// </summary>
    // shortcut: a finished journal is overwritten with nothing, never deleted, because a save store
    // has no delete. Each start reads every one, so the first login slows as pairs accumulate. Add a
    // delete to the store when the count gets large.
    public void SaveTogether(string a, string b)
    {
        var bytes = new OrderedDictionary<string, byte[]>();
        foreach (var player in new[] { a, b })
        {
            if (options.States.Capture(player) is { } state)
            {
                bytes[player] = state.Encode();
            }
        }

        var journal = StoreKeys.TradeOf(a, b);
        var whole = bytes.Count == 2;
        if (whole)
        {
            Write(journal, TradeJournal.Encode(bytes), new Custom("trade"));
        }

        foreach (var (player, state) in bytes)
        {
            Write(StoreKeys.SaveOf(player), state, new Custom("trade"));
        }

        if (whole)
        {
            ClearWhenSaved(journal, [.. bytes.Keys.Select(StoreKeys.SaveOf)]);
        }
    }

    /// <summary>A player has joined. Puts the state <see cref="PrepareAsync"/> read onto them.</summary>
    // F5: the join is the moment the world takes it, and no caller needs it.
    public void Arrived(string player)
    {
        PlayerState? state;
        lock (_gate)
        {
            _pending.Remove(player, out state);
        }

        if (state is null)
        {
            return;
        }

        var dropped = options.States.Apply(player, state);
        if (dropped > 0)
        {
            options.Log($"{player}: {dropped} saved entries were left out");
        }
    }

    /// <summary>A player's session ended. Call before the features forget them.</summary>
    public void Left(string player, GoneReason reason)
    {
        if (options.States.Capture(player) is not { } state)
        {
            return;
        }

        // One lock round both, so a login that is reading counts this save or is handed the state.
        lock (_gate)
        {
            if (_resuming.Remove(player))
            {
                _pending[player] = state;
            }

            Write(StoreKeys.SaveOf(player), state.Encode(), ReasonOf(reason));
        }
    }

    /// <summary>Saves the players the autosave names for <paramref name="tick"/>.</summary>
    public void Autosave(long tick)
    {
        foreach (var player in options.AutoSaver.Claim(options.States.Players, tick))
        {
            Save(player, new Autosave());
        }
    }

    /// <summary>Forgets the logins that took back a held session, once the host has seated them. Call it with the host's lock held.</summary>
    public void Sweep()
    {
        lock (_gate)
        {
            _resuming.RemoveWhere(player => options.SessionOf(player) == SessionState.Live);
        }
    }

    /// <summary>Saves everyone in the world and waits for the store. Call it once nothing else changes the world.</summary>
    public async Task ShutdownAsync()
    {
        Task[] clearing;
        lock (_gate)
        {
            clearing = [.. _clearing];
        }

        // A clear queued after the close would never reach a worker's store.
        await Task.WhenAll(clearing);
        foreach (var player in options.States.Players.ToList())
        {
            Save(player, new Shutdown());
        }

        var stuck = await options.Queue.CloseAsync();
        if (stuck.Count > 0)
        {
            options.Log($"Not written: {string.Join(", ", stuck)}");
        }
    }

    private static SaveReason ReasonOf(GoneReason reason) => reason switch
    {
        GoneReason.Removed => new Leave(),
        GoneReason.Disconnect or GoneReason.OverBudget => new Disconnect(),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private int QueuedOf(string key)
    {
        lock (_gate)
        {
            return _queued.GetValueOrDefault(key);
        }
    }

    // Called with the lock held. A player whose session is still held resumes with what the world
    // has, so a damaged save does not stand in the way of that.
    private void Hold(string player, SaveRecord? record, bool resuming)
    {
        _pending.Remove(player);
        if (resuming)
        {
            _resuming.Add(player);
        }
        else if (record is not null)
        {
            var state = PlayerState.Decode(record.Bytes.Span);
            options.States.Check(state);
            _pending[player] = state;
        }
    }

    /// <summary>
    /// Empties <paramref name="journal"/> once it and the saves named by <paramref name="saves"/>
    /// are on the store. A journal that is not emptied is replayed by the next start.
    /// </summary>
    private void ClearWhenSaved(string journal, IReadOnlyList<string> saves)
    {
        var run = ClearAsync(journal, saves);
        lock (_gate)
        {
            _clearing.RemoveAll(done => done.IsCompleted);
            _clearing.Add(run);
        }
    }

    // shortcut: flush waits for the queue to go idle, so under steady load a clear waits as long.
    // The journals stay until then. Give the queue a wait for named keys if that shows up in a profile.
    private async Task ClearAsync(string journal, IReadOnlyList<string> saves)
    {
        var stuck = await options.Queue.FlushAsync();

        // The keys are the same for a later trade of the pair, so a later write that failed holds
        // this clear back too.
        if (!stuck.Contains(journal) && !saves.Any(stuck.Contains))
        {
            Write(journal, [], new Custom("trade done"));
        }
    }

    /// <summary>Finishes the trades a crash cut short. Runs once, before the first login is read, and again after a failure.</summary>
    private async Task RecoverAsync()
    {
        Task run;
        lock (_gate)
        {
            run = _recovered ??= ReplayJournalsAsync();
        }

        try
        {
            await run;
        }
        catch
        {
            lock (_gate)
            {
                _recovered = null;
            }

            throw;
        }
    }

    private async Task ReplayJournalsAsync()
    {
        var found = await FindJournalsAsync();

        // A player in two journals takes the state of the newer one.
        var newest = new OrderedDictionary<string, (DateTimeOffset At, byte[] State)>();
        foreach (var journal in found.OrderBy(j => j.At))
        {
            foreach (var (player, state) in journal.States)
            {
                newest[player] = (journal.At, state);
            }
        }

        foreach (var (player, (at, state)) in newest)
        {
            await BringUpAsync(player, at, state);
        }

        foreach (var journal in found)
        {
            ClearWhenSaved(journal.Key, [.. journal.States.Keys.Select(StoreKeys.SaveOf)]);
        }
    }

    private async Task<List<Journal>> FindJournalsAsync()
    {
        var found = new List<Journal>();
        foreach (var key in await options.Queue.KeysAsync())
        {
            if (!key.StartsWith("t-", StringComparison.Ordinal))
            {
                continue;
            }

            var record = await options.Queue.ReadAsync(key);
            if (record is not null && !record.Bytes.IsEmpty && ReadJournal(key, record.Bytes.Span) is { } states)
            {
                found.Add(new Journal(key, record.At, states));
            }
        }

        return found;
    }

    /// <summary>
    /// The states in a journal, or null when it is damaged. A journal is read with the checks a
    /// login applies, so a bad one never goes over a save.
    /// </summary>
    private OrderedDictionary<string, byte[]>? ReadJournal(string key, ReadOnlySpan<byte> bytes)
    {
        try
        {
            var states = TradeJournal.Decode(bytes);
            foreach (var state in states.Values)
            {
                options.States.Check(PlayerState.Decode(state));
            }

            return states;
        }
        catch (FormatException e)
        {
            // shortcut: a damaged journal is skipped, so a cut-short trade it describes stays cut
            // short. Raise it to an operator alert later.
            options.Log($"Trade journal {key} is damaged and was left: {e.Message}");
            return null;
        }
    }

    private async Task BringUpAsync(string player, DateTimeOffset at, byte[] state)
    {
        var saved = await options.Queue.ReadAsync(StoreKeys.SaveOf(player));

        // shortcut: this compares the clock that stamped the journal with the one that stamped the
        // save. A clock set back across a crash can skip a recovery. Number the journals if that matters.
        if (saved is not null && saved.At > at)
        {
            return;
        }

        Write(StoreKeys.SaveOf(player), state, new Custom("trade recovered"));
        options.Log($"{player} was brought up to a trade a crash cut short");
    }

    private void Save(string player, SaveReason reason)
    {
        if (options.States.Capture(player) is { } state)
        {
            Write(StoreKeys.SaveOf(player), state.Encode(), reason);
        }
    }

    private void Write(string key, byte[] bytes, SaveReason reason)
    {
        // The count and the queue change under one lock, so a read that sees the count finds the save.
        lock (_gate)
        {
            _queued[key] = _queued.GetValueOrDefault(key) + 1;
            options.Queue.Save(key, bytes, reason);
        }
    }

    /// <summary>A trade journal read back: its key, when it was written, and the states.</summary>
    private sealed record Journal(string Key, DateTimeOffset At, OrderedDictionary<string, byte[]> States);
}
