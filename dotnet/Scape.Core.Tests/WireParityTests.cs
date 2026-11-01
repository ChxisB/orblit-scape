using System.Text.Json;
using System.Text.Json.Nodes;
using Orblit.Parity;
using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class WireParityTests
{
    private static readonly Fixture Golden = Fixture.Load("scape_core", "wire");

    // A list of cases is data: each message's fields, in the order its class declares them.
    private static readonly Dictionary<Type, Func<object, JsonNode?[]>> Fields = new()
    {
        [typeof(Login)] = Of<Login>(m => [m.Name, m.Password, m.Create]),
        [typeof(Walk)] = Of<Walk>(m => [new JsonArray(m.Target.X, m.Target.Y, m.Target.Plane)]),
        [typeof(Interact)] = Of<Interact>(m => [m.Kind, m.Id, m.Option]),
        [typeof(Chat)] = Of<Chat>(m => [m.Text]),
        [typeof(Whisper)] = Of<Whisper>(m => [m.To, m.Text]),
        [typeof(EditList)] = Of<EditList>(m => [NameOf(m.List), m.Add, m.Name]),
        [typeof(Buy)] = Of<Buy>(m => [m.Item, m.Count]),
        [typeof(Sell)] = Of<Sell>(m => [m.Item, m.Count]),
        [typeof(Deposit)] = Of<Deposit>(m => [m.Slot, m.Count, m.Tab]),
        [typeof(Withdraw)] = Of<Withdraw>(m => [m.Slot, m.Count]),
        [typeof(MoveItem)] = Of<MoveItem>(m => [NameOf(m.From), m.FromSlot, NameOf(m.To), m.ToSlot]),
        [typeof(TradeOffer)] = Of<TradeOffer>(m => [m.Item, m.Count]),
        [typeof(TradeAccept)] = _ => [],
        [typeof(TradeDecline)] = _ => [],
        [typeof(Cast)] = Of<Cast>(m => [m.Spell, m.Target]),
        [typeof(TogglePrayer)] = Of<TogglePrayer>(m => [m.Prayer]),
        [typeof(Continue)] = _ => [],
        [typeof(Choose)] = Of<Choose>(m => [m.Index]),
        [typeof(LoginOk)] = Of<LoginOk>(m => [m.Name]),
        [typeof(Avatar)] = Of<Avatar>(m => [m.NetworkId]),
        [typeof(PrivateState)] = Of<PrivateState>(m => [Convert.ToHexStringLower(m.Delta.Span)]),
        [typeof(Notice)] = Of<Notice>(m => [m.Text]),
        [typeof(Hit)] = Of<Hit>(m => [m.Target, m.Damage]),
        [typeof(Projectile)] = Of<Projectile>(m => [m.From, m.To, m.Ticks, NameOf(m.Style)]),
        [typeof(PrayerView)] = Of<PrayerView>(m => [m.Points, m.Max, Dump.Arr(m.Active, id => id)]),
        [typeof(Said)] = Of<Said>(m => [m.Speaker, m.Text]),
        [typeof(Whispered)] = Of<Whispered>(m => [m.From, m.Text]),
        [typeof(SocialLists)] = Of<SocialLists>(m =>
            [Dump.Arr(m.Friends, friend => new JsonArray(friend.Name, friend.Online)), Dump.Arr(m.Ignored, Dump.Text)]),
        [typeof(ShopView)] = Of<ShopView>(m => [m.Npc, Dump.Arr(m.Lines, line => new JsonArray(line.Item, line.Count))]),
        [typeof(BankOpen)] = _ => [],
        [typeof(WindowClosed)] = _ => [],
        [typeof(TradeView)] = Of<TradeView>(m =>
            [m.Partner, Stacks(m.Mine), Stacks(m.Theirs), m.Step.Confirming, m.Step.YouAccepted, m.Step.TheyAccepted]),
        [typeof(ConversationLine)] = Of<ConversationLine>(m => [m.Speaker, m.Text]),
        [typeof(ConversationChoices)] = Of<ConversationChoices>(m => [Dump.Arr(m.Options, Dump.Text)]),
    };

    [Fact]
    public void EveryMessageBuiltAtItsEdgesReadsAndWritesAsTheFixtureDoes() => Golden.Check("built", Read);

    [Fact]
    public void DamagedRandomAndOddTextBytesGetTheFixtureAnswerFromEachDecoder() => Golden.Check("bytes", Read);

    [Fact]
    public void SlotDeltasAreWrittenAsTheFixtureWritesThem() => Golden.Check("encodeSlots", EncodeSlots);

    [Fact]
    public void SlotDeltasAreReadAsTheFixtureReadsThem() => Golden.Check("readSlots", ReadSlots);

    [Fact]
    public void PlayerStateBytesReadAndWriteAsTheFixtureDoes() =>
        Golden.Check("state", c => Dump.Attempt(() => Hex(PlayerState.Decode(BytesOf(c)).Encode())));

    private static Func<object, JsonNode?[]> Of<T>(Func<T, JsonNode?[]> fields) => message => fields((T)message);

    private static JsonArray Describe(object message) => new([message.GetType().Name, .. Fields[message.GetType()](message)]);

    private static JsonArray Stacks(IReadOnlyList<ItemStack> stacks) =>
        Dump.Arr(stacks, stack => new JsonArray(stack.Item, stack.Count));

    private static string NameOf(Enum value) => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static byte[] BytesOf(JsonElement c) => Convert.FromHexString(c.GetProperty("bytes").GetString()!);

    private static JsonObject Read(JsonElement c)
    {
        var bytes = BytesOf(c);
        return new JsonObject
        {
            ["client"] = Outcome(MessageCodec.DecodeClient(bytes), MessageCodec.Encode),
            ["server"] = Outcome(MessageCodec.DecodeServer(bytes), MessageCodec.Encode),
        };
    }

    /// <summary>The message and the bytes the codec writes for it, or why the codec refused the bytes.</summary>
    private static JsonObject Outcome<T>(Decoded<T> decoded, Func<T, byte[]> encode)
        where T : class =>
        decoded is Understood<T> { Message: var message }
            ? new JsonObject { ["message"] = Describe(message), ["bytes"] = Hex(encode(message)) }
            : new JsonObject { ["why"] = NameOf(((Unreadable<T>)decoded).Why) };

    private static JsonArray EncodeSlots(JsonElement c)
    {
        var id = (ContainerId)c.GetProperty("in").GetInt32();
        var bag = Container.ForId(id, Items());
        bag.Mirror(c.GetProperty("held").EnumerateArray()
            .Select(row => new SlotChange(row[0].GetInt32(), new ItemStack(row[1].GetInt32(), row[2].GetInt32()))));
        var slots = c.GetProperty("slots").EnumerateArray().Select(slot => slot.GetInt32());
        return Dump.Arr(ContainerCodec.EncodeSlots(id, bag, slots), delta => Hex(delta));
    }

    private static JsonArray? ReadSlots(JsonElement c) =>
        ContainerCodec.TryDecodeSlots(BytesOf(c), Items()) is { } delta
            ? new JsonArray(NameOf(delta.Container), Dump.Arr(delta.Changes, Change))
            : null;

    private static JsonArray Change(SlotChange change) =>
        change.Stack is { } stack ? new JsonArray(change.Slot, stack.Item, stack.Count) : new JsonArray(change.Slot);
}
