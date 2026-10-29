using System.Security.Cryptography;
using Orblit.Save;

namespace Scape.Server;

/// <summary>Where a server keeps its players and how it checks who they are.</summary>
/// <remarks>
/// Without a <see cref="Hasher"/> there are no accounts and a name is the whole of an identity,
/// which suits a test or a private game. With one, a login names an account and gives its password.
/// </remarks>
public sealed record Storage
{
    public required SaveQueue Queue { get; init; }

    public IPasswordHasher? Hasher { get; init; }

    /// <summary>Makes that many random bytes for an account's salt. A test passes fixed ones.</summary>
    public Func<int, byte[]> Salt { get; init; } = RandomNumberGenerator.GetBytes;

    public AutoSaver AutoSaver { get; init; } = new(perTick: 100, every: 500, jitter: 100, new Random());
}
