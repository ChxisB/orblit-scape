using System.Collections.Concurrent;
using Orblit.Save;

namespace Scape.Server.Tests;

/// <summary>Where a test server keeps its saves.</summary>
internal static class Stores
{
    /// <summary>
    /// Storage on <paramref name="store"/> through an inline queue. The clock steps unless the test
    /// passes one, and a write that fails is dropped.
    /// </summary>
    public static Storage On(ISaveStore store, Func<DateTimeOffset>? clock = null, IPasswordHasher? hasher = null) => new()
    {
        Queue = SaveQueue.Inline(store, clock ?? Stepping(), (_, _) => { }),
        Hasher = hasher,
    };

    /// <summary>A clock that moves one second each time it is read, from the start of 2026.</summary>
    public static Func<DateTimeOffset> Stepping()
    {
        var seconds = -1;
        return () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref seconds));
    }

    /// <summary>A gate a test opens by hand. What waits on it goes on off the test's stack.</summary>
    public static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>A hash a test can wait for: the password and the salt, mixed.</summary>
internal sealed class FastHasher : IPasswordHasher
{
    private int _calls;

    /// <summary>When set, a hash waits for it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>How many hashes were asked for.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public async Task<byte[]> HashAsync(string password, byte[] salt)
    {
        Interlocked.Increment(ref _calls);
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        var digest = new byte[8];
        for (var i = 0; i < digest.Length; i++)
        {
            digest[i] = (byte)((password[i % password.Length] * 31) + salt[i % salt.Length] + i);
        }

        return digest;
    }
}

/// <summary>
/// A store a test can hold. A read finds the save first and then waits for
/// <see cref="ReadGate"/>, so it returns what was true when it began. A write waits for
/// <see cref="WriteGate"/>.
/// </summary>
internal sealed class GatedStore(ISaveStore inner) : ISaveStore
{
    public ISaveStore Inner => inner;

    public TaskCompletionSource? ReadGate { get; set; }

    public TaskCompletionSource? WriteGate { get; set; }

    public async Task<SaveRecord?> ReadAsync(string key)
    {
        var found = await inner.ReadAsync(key);
        if (ReadGate is { } gate)
        {
            await gate.Task;
        }

        return found;
    }

    public async Task WriteAsync(string key, SaveRecord record)
    {
        if (WriteGate is { } gate)
        {
            await gate.Task;
        }

        await inner.WriteAsync(key, record);
    }

    public Task<bool> ExistsAsync(string key) => inner.ExistsAsync(key);

    public Task<IReadOnlyList<string>> KeysAsync() => inner.KeysAsync();
}

/// <summary>A store that stops writing, with no error, after <see cref="Budget"/> writes, as a crash would.</summary>
internal sealed class CrashingStore(ISaveStore inner) : ISaveStore
{
    /// <summary>How many more writes reach the store, or null for all of them.</summary>
    public int? Budget { get; set; }

    public Task WriteAsync(string key, SaveRecord record)
    {
        if (Budget is { } left)
        {
            if (left <= 0)
            {
                return Task.CompletedTask;
            }

            Budget = left - 1;
        }

        return inner.WriteAsync(key, record);
    }

    public Task<SaveRecord?> ReadAsync(string key) => inner.ReadAsync(key);

    public Task<bool> ExistsAsync(string key) => inner.ExistsAsync(key);

    public Task<IReadOnlyList<string>> KeysAsync() => inner.KeysAsync();
}

/// <summary>A store whose first listing of keys fails, as one that is not up yet would.</summary>
internal sealed class SlowStartStore(ISaveStore inner) : ISaveStore
{
    private int _listed;

    public Task<IReadOnlyList<string>> KeysAsync() => Interlocked.Exchange(ref _listed, 1) == 0
        ? Task.FromException<IReadOnlyList<string>>(new IOException("the store is not up yet"))
        : inner.KeysAsync();

    public Task<SaveRecord?> ReadAsync(string key) => inner.ReadAsync(key);

    public Task WriteAsync(string key, SaveRecord record) => inner.WriteAsync(key, record);

    public Task<bool> ExistsAsync(string key) => inner.ExistsAsync(key);
}

/// <summary>A store that records its writes and fails the keys in <see cref="Failing"/>.</summary>
internal sealed class SpyStore(ISaveStore inner) : ISaveStore
{
    // Writes arrive on the pool while a test reads them.
    private readonly ConcurrentQueue<(string Key, bool Empty)> _writes = [];

    /// <summary>Each write that was asked for, oldest first, and whether it held nothing.</summary>
    public IReadOnlyList<(string Key, bool Empty)> Writes => [.. _writes];

    /// <summary>Keys whose writes throw. Change it only while no write is in flight.</summary>
    public HashSet<string> Failing { get; } = [];

    public Task WriteAsync(string key, SaveRecord record)
    {
        if (Failing.Contains(key))
        {
            throw new IOException($"write of {key} failed");
        }

        _writes.Enqueue((key, record.Bytes.IsEmpty));
        return inner.WriteAsync(key, record);
    }

    public Task<SaveRecord?> ReadAsync(string key) => inner.ReadAsync(key);

    public Task<bool> ExistsAsync(string key) => inner.ExistsAsync(key);

    public Task<IReadOnlyList<string>> KeysAsync() => inner.KeysAsync();
}
