using Scape.Core;

namespace Scape.Server;

/// <summary>
/// What a login must pass before the host seats the player: the account when the server has
/// them, then the save.
/// </summary>
internal sealed class Entrance(Saves saves, Accounts? accounts)
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

        await saves.PrepareAsync(player);
        return null;
    }
}
