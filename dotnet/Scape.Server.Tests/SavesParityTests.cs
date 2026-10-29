using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using Orblit.Save;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class SavesParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_server", "saves");

    /// <summary>The time the exporter's queue stamped every record with.</summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    // Store keys.

    [Fact]
    public void KeysMatchTheFixture() => Golden.Check("key", c =>
    {
        var name = c.GetProperty("name").GetString()!;
        return new JsonArray(StoreKeys.SaveOf(name), StoreKeys.AccountOf(name));
    });

    [Fact]
    public void TradeKeysMatchTheFixture() =>
        Golden.Check("trade", c => Dump.Text(StoreKeys.TradeOf(c.GetProperty("a").GetString()!, c.GetProperty("b").GetString()!)));

    // Trade journals.

    [Fact]
    public void WrittenJournalsMatchTheFixture() => Golden.Check("journalWrite", c => Dump.Text(Encoding.UTF8.GetString(
        TradeJournal.Encode(c.GetProperty("states").EnumerateArray().Select(state => KeyValuePair.Create(state[0].GetString()!, Bytes(state[1])))))));

    [Fact]
    public void ReadJournalsMatchTheFixture() => Golden.Check("journalRead", c => Dump.Attempt(() =>
        Dump.Arr(TradeJournal.Decode(Bytes(c.GetProperty("bytes"))), state => new JsonArray(state.Key, Hex(state.Value)))));

    // Accounts.

    [Fact]
    public void MadeAccountsMatchTheFixture() => Golden.Check("create", Created);

    [Fact]
    public void CheckedLoginsMatchTheFixture() => Golden.Check("verify", Verified);

    private static byte[] Bytes(JsonElement hex) => Convert.FromHexString(hex.GetString()!);

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static SaveQueue Queue() => SaveQueue.Inline(new MemorySaveStore(), () => Start, (_, _) => { });

    // The fixture checks a case with a plain call. The store and the hasher here finish at once.
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static JsonObject Created(JsonElement c)
    {
        var name = c.GetProperty("name").GetString()!;
        var queue = Queue();
        if (c.GetProperty("taken").GetString() is { } taken)
        {
            queue.Save(taken, [], new Custom("taken"));
        }

        var salt = Bytes(c.GetProperty("salt"));
        var refusal = Wait(new Accounts(queue, new PlainHasher(), _ => salt).CreateAsync(name, c.GetProperty("password").GetString()!));
        var record = refusal is null ? Wait(queue.ReadAsync(StoreKeys.AccountOf(name))) : null;
        return new JsonObject
        {
            ["refusal"] = refusal?.ToWire(),
            ["record"] = record is null ? null : Encoding.UTF8.GetString(record.Bytes.Span),
        };
    }

    /// <summary>A login as a, against the case's record or against no account.</summary>
    private static JsonObject Verified(JsonElement c)
    {
        var queue = Queue();
        if (c.GetProperty("record").ValueKind != JsonValueKind.Null)
        {
            queue.Save(StoreKeys.AccountOf("a"), Bytes(c.GetProperty("record")), new Custom("account"));
        }

        var accounts = new Accounts(queue, new PlainHasher(), _ => []);
        return Dump.Attempt(() => Wait(accounts.VerifyAsync("a", c.GetProperty("password").GetString()!))?.ToWire());
    }

    /// <summary>The exporter's stand-in for the slow hash: the salt, then the password's UTF-8.</summary>
    private sealed class PlainHasher : IPasswordHasher
    {
        public Task<byte[]> HashAsync(string password, byte[] salt) => Task.FromResult<byte[]>([.. salt, .. Encoding.UTF8.GetBytes(password)]);
    }
}
