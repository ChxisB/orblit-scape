using System.Globalization;
using Orblit.Save;

namespace Scape.Server;

/// <summary>What <see cref="Rollback"/> works with.</summary>
internal sealed record RollbackOptions
{
    public required HistoryStore History { get; init; }

    public required SaveQueue Queue { get; init; }

    public required Ledger Ledger { get; init; }

    /// <summary>Ends the player's session. It completes once their leave save is queued.</summary>
    public required Func<string, Task> LogOut { get; init; }

    /// <summary>Whether the player has a session, live or held.</summary>
    public required Func<string, bool> IsOnline { get; init; }

    /// <summary>The clock the queue stamps saves with, so a save and a rollback agree on the time.</summary>
    public required Func<DateTimeOffset> Now { get; init; }
}

/// <summary>
/// Puts a player's save back to how it was some minutes ago, from the history the store keeps.
/// The player is logged out first, because the game writes a live player's save over anything put
/// there.
/// </summary>
internal sealed class Rollback(RollbackOptions options)
{
    /// <summary>
    /// Rolls <paramref name="player"/> back <paramref name="minutes"/> and says what happened. A
    /// refusal is an answer, so this does not throw. The player cannot log in while it runs.
    /// </summary>
    // F5: the answer is the report of what was done, and a caller that checked the lock first could
    // race another rollback.
    public async Task<string> RunAsync(string player, int minutes)
    {
        if (!options.Ledger.Lock(player))
        {
            return $"A rollback of {player} is already running.";
        }

        try
        {
            return await RestoreAsync(player, minutes);
        }
#pragma warning disable CA1031 // Any failure is told to the admin who asked, and the lock still comes off.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return $"The rollback of {player} failed: {e.Message}";
        }
        finally
        {
            options.Ledger.Unlock(player);
        }
    }

    private async Task<string> RestoreAsync(string player, int minutes)
    {
        await options.LogOut(player);

        // The save the leave just queued is the newest version, and it must be on the store before
        // the restore moves the current save.
        var key = StoreKeys.SaveOf(player);
        if ((await options.Queue.FlushAsync()).Contains(key))
        {
            return $"The save of {player} is not on the store yet. Try again.";
        }

        // Counted in ticks, since enough minutes reach back past the year 1.
        var cutoff = options.Now().UtcTicks - (minutes * TimeSpan.TicksPerMinute);
        var versions = await options.History.History.ListAsync(key);
        if (versions.FirstOrDefault(v => v.At.UtcTicks <= cutoff) is not { } target)
        {
            return FormattableString.Invariant($"{player} has no save from {minutes} minutes ago.");
        }

        await new SaveTools(options.History, _ => options.IsOnline(player), options.Now).RestoreAsync(key, target.At);
        var at = target.At.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return $"{player} was rolled back to {at}.";
    }
}
