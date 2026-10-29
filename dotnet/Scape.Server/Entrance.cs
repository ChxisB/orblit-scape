using Scape.Core;

namespace Scape.Server;

/// <summary>
/// What a login must pass before the host seats the player: the account when the server has
/// them, the ban, then the save. <c>prepare</c> reads the player's save.
/// </summary>
internal sealed class Entrance(Func<string, Task> prepare, Accounts? accounts, Ledger? ledger)
{
    /// <summary>Null when <paramref name="login"/> may join, or why it may not. <paramref name="player"/> is the key.</summary>
    // F5: a login that is allowed must also make its account and load its save, and one step would
    // race the other.
    public async Task<LoginRefusal?> CheckAsync(string player, Login login)
    {
        if (accounts is not null)
        {
            var why = login.Create
                ? await accounts.CreateAsync(player, login.Password)
                : await accounts.VerifyAsync(player, login.Password);
            if (why is not null)
            {
                return why;
            }
        }

        if (await ScreenAsync(player) is { } refused)
        {
            return refused;
        }

        await prepare(player);

        // A ban or a rollback may have begun while the save was read.
        return await ScreenAsync(player);
    }

    /// <summary>
    /// Why the ledger keeps <paramref name="player"/> out: a ban, or a lock while their save is
    /// rolled back. The ban is checked after the password, so a wrong guess never learns who is
    /// banned.
    /// </summary>
    private async Task<LoginRefusal?> ScreenAsync(string player)
    {
        if (ledger is null)
        {
            return null;
        }

        await ledger.LoadAsync();
        if (ledger.Active(SanctionKind.Ban, player) is not null)
        {
            return LoginRefusal.Banned;
        }

        return ledger.IsLocked(player) ? LoginRefusal.Unavailable : null;
    }
}
