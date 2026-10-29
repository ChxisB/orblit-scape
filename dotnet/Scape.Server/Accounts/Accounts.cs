using System.Security.Cryptography;
using System.Text;
using Orblit.Json;
using Orblit.Save;
using Scape.Core;

namespace Scape.Server;

/// <summary>Turns a password and a salt into the digest an account keeps.</summary>
public interface IPasswordHasher
{
    Task<byte[]> HashAsync(string password, byte[] salt);
}

/// <summary>
/// A name and a salted, slow hash of a password for each player. The record goes through the same
/// <see cref="SaveQueue"/> as saves, so the tick never waits on the disk for it and history keeps
/// what it held.
/// </summary>
/// <remarks>
/// A check holds one of <see cref="MaxInFlight"/> places while it reads and hashes. When they are
/// all taken, the next login hears busy at once, so a flood of logins costs a refusal each and not
/// a hash each.
/// </remarks>
// shortcut: a wrong login costs the same as a right one and nothing counts failures per name or
// address, so a guesser is slowed only by the hash, and a client that keeps the places taken makes
// other logins hear busy. A repeated wrong login for a name also keeps that name's own login busy.
// Add a lockout and a limit for each address when the server is public.
// shortcut: the record names no cost, so version 1 means Argon2id with the numbers in the hasher.
// A change to them adds a record version that carries its own, and a successful login can then
// rewrite the old one.
internal sealed class Accounts(SaveQueue queue, IPasswordHasher hasher, Func<int, byte[]> salt)
{
    private const int MaxInFlight = 4;
    private const int SaltBytes = 16;
    private const int RecordVersion = 1;

    private int _inFlight;

    /// <summary>
    /// Makes the account for <paramref name="player"/>, who is a key, or says why not. A password
    /// under <see cref="Wire.MinPasswordBytes"/> is weak, and a name that has an account is taken.
    /// </summary>
    public Task<LoginRefusal?> CreateAsync(string player, string password)
    {
        if (Encoding.UTF8.GetByteCount(password) < Wire.MinPasswordBytes)
        {
            return Task.FromResult<LoginRefusal?>(LoginRefusal.WeakPassword);
        }

        return LimitedAsync(async () =>
        {
            var key = StoreKeys.AccountOf(player);

            // A character with no account is still somebody's, so it is taken.
            if (await queue.ExistsAsync(key) || await queue.ExistsAsync(StoreKeys.SaveOf(player)))
            {
                return LoginRefusal.AccountExists;
            }

            var grain = salt(SaltBytes);
            var digest = await hasher.HashAsync(password, grain);
            queue.Save(key, Encode(grain, digest), new Custom("account"));
            return null;
        });
    }

    /// <summary>
    /// Checks <paramref name="password"/> against the account of <paramref name="player"/>, or says
    /// why not. An account that does not exist is a bad login too. A miss answers faster than a hit,
    /// because it skips the hash, and creating an account tells whether a name is free, so names are
    /// not private.
    /// </summary>
    public Task<LoginRefusal?> VerifyAsync(string player, string password) => LimitedAsync(async () =>
    {
        var record = await queue.ReadAsync(StoreKeys.AccountOf(player));
        if (record is null)
        {
            return LoginRefusal.BadLogin;
        }

        var (grain, digest) = Decode(record.Bytes.Span);
        var again = await hasher.HashAsync(password, grain);
        return CryptographicOperations.FixedTimeEquals(digest, again) ? null : LoginRefusal.BadLogin;
    });

    private static byte[] Encode(byte[] salt, byte[] digest) => Encoding.UTF8.GetBytes(JsonText.Encode(new OrderedDictionary<string, object?>
    {
        ["version"] = (long)RecordVersion,
        ["salt"] = Convert.ToBase64String(salt),
        ["hash"] = Convert.ToBase64String(digest),
    }));

    /// <summary>Reads what <see cref="Encode"/> wrote. Throws <see cref="FormatException"/> for anything else.</summary>
    private static (byte[] Salt, byte[] Digest) Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            // JSON does not tell 1 from 1.0, so either is version 1.
            if (JsonText.Decode(Wire.DecodeText(bytes)) is OrderedDictionary<string, object?> record
                && record.GetValueOrDefault("version") is (long)RecordVersion or (double)RecordVersion
                && record.GetValueOrDefault("salt") is string salt
                && record.GetValueOrDefault("hash") is string hash)
            {
                return (StoredBase64.Decode(salt), StoredBase64.Decode(hash));
            }
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            // Falls through to the one error below.
        }

        throw new FormatException("Not an account record");
    }

    private async Task<LoginRefusal?> LimitedAsync(Func<Task<LoginRefusal?>> work)
    {
        if (Interlocked.Increment(ref _inFlight) > MaxInFlight)
        {
            Interlocked.Decrement(ref _inFlight);
            return LoginRefusal.Busy;
        }

        try
        {
            return await work();
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
