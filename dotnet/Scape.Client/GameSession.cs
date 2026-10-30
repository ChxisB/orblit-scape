using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using Orblit.Core;
using Orblit.Net;
using Orblit.Progress;
using Scape.Content;
using Scape.Core;

namespace Scape.Client;

/// <summary>What a <see cref="GameSession"/> is made with.</summary>
public sealed class GameSessionOptions
{
    public required SkillSet Skills { get; init; }

    public required ItemSet Items { get; init; }

    /// <summary>The quests the game has. Without it the journal is always empty.</summary>
    public Story? Story { get; init; }

    /// <summary>The time on the client's clock. It reads a stopwatch from the session's start when left out.</summary>
    public Func<TimeSpan>? Clock { get; init; }
}

/// <summary>
/// The client's side of one login: what it sends, what it has been told and the actors it can
/// see. It holds no rules and draws what it is sent.
/// </summary>
/// <remarks>
/// The server's messages arrive on the link's thread. They wait until <see cref="Advance"/>, so
/// every member but <see cref="Refusal"/> belongs to the one thread that draws.
/// </remarks>
public sealed class GameSession : IAsyncDisposable
{
    /// <summary>A little over one tick, so the client always holds a state on each side of the time it draws.</summary>
    public static readonly TimeSpan InterpolationDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>How long a line of chat stays over a head.</summary>
    public const double BubbleSeconds = 5;

    /// <summary>The most notices and whispers kept. The oldest go first.</summary>
    private const int KeepLines = 100;

    private readonly ITransport _transport;
    private readonly SkillSet _skills;
    private readonly ItemSet _items;
    private readonly Story? _story;
    private readonly Func<TimeSpan> _clock;
    private readonly Dictionary<ContainerId, Container> _containers;
    private readonly ActorAnimator _animator = new();
    private readonly ConcurrentQueue<ServerMessage> _inbox = [];
    private readonly World _world = new();
    private readonly ComponentType _position;
    private readonly ComponentType _ground;
    private readonly ComponentType _object;
    private readonly ComponentType _npc;
    private readonly ComponentType _health;
    private readonly ComponentType _look;
    private readonly ComponentType _anim;
    private readonly NetClient _client;
    private readonly List<string> _notices = [];
    private readonly List<Heard> _whispers = [];
    private readonly List<Spoken> _said = [];
    private readonly List<Splat> _splats = [];
    private readonly List<Shot> _shots = [];
    private readonly Dictionary<long, (string Text, double Until)> _bubbles = [];
    private ProgressTable _skillXp;
    private ProgressTable? _questTable;
    private volatile string? _refusal;

    public GameSession(ITransport transport, GameSessionOptions options)
    {
        _transport = transport;
        _skills = options.Skills;
        _items = options.Items;
        _story = options.Story;
        _containers = Enum.GetValues<ContainerId>().ToDictionary(id => id, id => Container.ForId(id, options.Items));
        _skillXp = new ProgressTable(options.Skills.Schema);
        _questTable = options.Story is null ? null : new ProgressTable(options.Story.Schema);
        var started = Stopwatch.StartNew();
        _clock = options.Clock ?? (() => started.Elapsed);

        var networkId = _world.RegisterComponent("NetworkId", ComponentKind.Int64);
        _position = _world.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        _ground = _world.RegisterComponent(GroundItem.Name, ComponentKind.Int32, GroundItem.Ints);
        _object = _world.RegisterComponent(WorldObject.Name, ComponentKind.Int32, WorldObject.Ints);
        _npc = _world.RegisterComponent(Npc.Name, ComponentKind.Int32, Npc.Ints);
        _health = _world.RegisterComponent(Health.Name, ComponentKind.Int32, Health.Ints);
        _look = _world.RegisterComponent(Look.Name, ComponentKind.Int32, Look.Ints);
        _anim = _world.RegisterComponent(Anim.Name, ComponentKind.Int32, Anim.Ints);
        _client = new NetClient(new NetClientOptions
        {
            World = _world,
            Set = new ReplicationSet([_position, _ground, _object, _npc, _health, _look, _anim]),
            NetworkId = networkId,
            Transport = transport,
            InterpolationDelay = InterpolationDelay,
            Clock = _clock,
            OnMessage = Receive,
            OnGoodbye = reason => _refusal = reason,
        });
    }

    /// <summary>Opens a connection to the server at <paramref name="url"/>. The session closes it on <see cref="DisposeAsync"/>.</summary>
    /// <exception cref="TimeoutException">Nothing answered in time.</exception>
    public static async Task<GameSession> ConnectAsync(Uri url, GameSessionOptions options) =>
        new(await SocketTransport.ConnectAsync(url), options);

    /// <summary>The name the server knows the player by, once logged in.</summary>
    public string? Name { get; private set; }

    /// <summary>Which replicated entity is the player's own, once the server says.</summary>
    public long? AvatarId { get; private set; }

    /// <summary>Why the server sent the player away, if it did. It is set from the link's thread.</summary>
    public string? Refusal => _refusal;

    /// <summary>Friends, with whether each is logged in. Empty until the server sends it.</summary>
    public IReadOnlyList<Friend> Friends { get; private set; } = [];

    /// <summary>Names the player ignores.</summary>
    public IReadOnlyList<string> Ignored { get; private set; } = [];

    /// <summary>The shop the player has open, as the server last sent it, or null. A shop, a bank, a trade and a conversation are never open together.</summary>
    public ShopView? Shop { get; private set; }

    /// <summary>The trade with another player the player has open, as the server last sent it, or null.</summary>
    public TradeView? PlayerTrade { get; private set; }

    /// <summary>The player's prayer points and which prayers are on, as the server last sent them, or null before it has.</summary>
    public PrayerView? Prayer { get; private set; }

    /// <summary>Whether the player's bank is open. <see cref="ItemAt"/> reads what is in it.</summary>
    public bool IsBankOpen { get; private set; }

    /// <summary>The line a creature is saying in the open conversation, or null.</summary>
    public ConversationLine? Line { get; private set; }

    /// <summary>What the player may pick in the open conversation, in the order the server sent. Empty when it is showing a line or none is open.</summary>
    public IReadOnlyList<string> Choices { get; private set; } = [];

    public IReadOnlyList<string> Notices => [.. _notices];

    /// <summary>Private messages received, oldest first.</summary>
    public IReadOnlyList<Heard> Whispers => [.. _whispers];

    /// <summary>Swings that landed or missed, oldest first.</summary>
    public IReadOnlyList<Splat> Splats => [.. _splats];

    /// <summary>Shots and spells that were sent, oldest first.</summary>
    public IReadOnlyList<Shot> Shots => [.. _shots];

    public bool IsLoggedIn => Name is not null;

    /// <summary>
    /// The chat lines to draw now, one per speaker, the newest of each. A speaker is one entry for as
    /// long as the session lasts, which is small.
    /// </summary>
    public IReadOnlyList<Bubble> Bubbles =>
        [.. _bubbles.Where(pair => pair.Value.Until > Now).Select(pair => new Bubble(pair.Key, pair.Value.Text))];

    /// <summary>The quests the player has begun, with the text for the stage they are in. Empty when the session was made without a story.</summary>
    public IReadOnlyList<JournalEntry> Journal =>
        _story is null || _questTable is null
            ? []
            : [.. _story.Book.Quests
                .Where(quest => _story.Book.IsStarted(_questTable, quest.Key))
                .Select(quest => new JournalEntry(
                    quest.Title,
                    _story.Book.Journal(_questTable, quest.Key),
                    _story.Book.IsComplete(_questTable, quest.Key)))];

    /// <summary>Every actor in view, with the position it is drawn at now.</summary>
    public IReadOnlyList<ActorSprite> Actors =>
        [.. InView()
            .Where(seen => seen.Ground is null && seen.Object is null && seen.Npc is null)
            .Select(seen => new ActorSprite(seen.NetworkId, seen.At.X, seen.At.Y, seen.At.Plane, seen.NetworkId == AvatarId))];

    /// <summary>Every object in view, such as a door or a tree, with its state.</summary>
    public IReadOnlyList<ObjectSprite> Objects =>
        [.. InView().Where(seen => seen.Object is not null).Select(seen => new ObjectSprite(
            seen.NetworkId, Int(seen.Object!, 0), Int(seen.Object!, 1), seen.At.X, seen.At.Y, seen.At.Plane))];

    /// <summary>Every creature in view, such as a chicken or a guard.</summary>
    public IReadOnlyList<NpcSprite> Npcs =>
        [.. InView().Where(seen => seen.Npc is not null).Select(seen => new NpcSprite(
            seen.NetworkId, Int(seen.Npc!, 0), seen.At.X, seen.At.Y, seen.At.Plane))];

    /// <summary>Every item lying in view, with the position it is drawn at now.</summary>
    public IReadOnlyList<GroundSprite> GroundItems =>
        [.. InView().Where(seen => seen.Ground is not null).Select(seen => new GroundSprite(
            seen.NetworkId, Int(seen.Ground!, 0), Int(seen.Ground!, 1), seen.At.X, seen.At.Y, seen.At.Plane))];

    private double Now => _clock().TotalSeconds;

    /// <summary>
    /// The lines of <paramref name="tab"/>, oldest first, as the chat box shows them. The server sends
    /// no names with public chat, so a line reads "You" or "Someone".
    /// </summary>
    public IReadOnlyList<string> ChatOf(ChatTab tab) => tab switch
    {
        ChatTab.Game => Notices,
        ChatTab.Private => [.. _whispers.Select(heard => $"{heard.From}: {heard.Text}")],
        _ => [.. _said.Select(spoken => $"{(spoken.Speaker == AvatarId ? "You" : "Someone")}: {spoken.Text}")],
    };

    /// <summary>The hit points of the actor <paramref name="networkId"/> names, or null for one that is not in view or does not fight.</summary>
    public HitPoints? HealthOf(long networkId) =>
        BytesOf(networkId, _health) is { } bytes ? new HitPoints(Int(bytes, 0), Int(bytes, 1)) : null;

    /// <summary>How the actor <paramref name="networkId"/> names looks, or null for one that is not in view or is not a player.</summary>
    public Look? LookOf(long networkId) => BytesOf(networkId, _look) is { } bytes ? Look.TryRead(bytes) : null;

    /// <summary>What the actor <paramref name="networkId"/> names is doing, or null for one that is not in view.</summary>
    public Anim? AnimOf(long networkId) => BytesOf(networkId, _anim) is { } bytes ? Anim.TryRead(bytes) : null;

    /// <summary>
    /// The pose to draw the actor <paramref name="networkId"/> names in now, or null for one that is not in
    /// view. Ask once a frame, after <see cref="Advance"/>, for each actor drawn.
    /// </summary>
    // F5: sampling is what notices a new act, so it records what it saw.
    public Pose? PoseOf(long networkId) =>
        AnimOf(networkId) is { } anim ? _animator.Sample((ulong)networkId, anim, Now) : null;

    /// <summary>
    /// The experience the server has told the player they hold in <paramref name="skill"/>.
    /// Throws <see cref="ArgumentException"/> for a skill the game does not have.
    /// </summary>
    public long XpOf(string skill) => _skillXp.Get(SkillSet.XpName(Skill(skill).Id));

    /// <summary>The level that experience gives, up to the skill's cap.</summary>
    public int LevelOf(string skill) => _skills.LevelOf(Skill(skill), XpOf(skill));

    /// <summary>What the server has told the player is in <paramref name="slot"/> of <paramref name="container"/>, or null for an empty slot and for a slot that does not exist.</summary>
    public ItemStack? ItemAt(ContainerId container, int slot) => _containers[container].At(slot);

    /// <summary>Joins as <paramref name="player"/>. A server with accounts wants the <paramref name="password"/>, and <paramref name="create"/> makes the account first.</summary>
    public void Login(string player, string password = "", bool create = false) =>
        _client.Join(MessageCodec.Encode(new Login(player, password, create)));

    public void WalkTo(TileCoord tile) => Send(new Walk(tile));

    /// <summary>Picks option <paramref name="option"/> of the object <paramref name="networkId"/> names.</summary>
    public void UseObject(long networkId, int option) => Send(new Interact(InteractKind.PlacedObject, networkId, option));

    /// <summary>Picks up the item lying on the ground that <paramref name="networkId"/> names.</summary>
    public void TakeItem(long networkId) => Send(new Interact(InteractKind.Ground, networkId, 0));

    /// <summary>Attacks the creature that <paramref name="networkId"/> names.</summary>
    public void Attack(long networkId) => Send(new Interact(InteractKind.Npc, networkId, NpcOption.Attack));

    /// <summary>Casts the spell <paramref name="spell"/> at the creature that <paramref name="networkId"/> names.</summary>
    public void Cast(int spell, long networkId) => Send(new Cast(spell, networkId));

    /// <summary>Switches the prayer <paramref name="prayer"/> on or off.</summary>
    public void TogglePrayer(int prayer) => Send(new TogglePrayer(prayer));

    /// <summary>Opens the shop of the creature that <paramref name="networkId"/> names.</summary>
    public void Trade(long networkId) => Send(new Interact(InteractKind.Npc, networkId, NpcOption.Trade));

    /// <summary>Talks to the creature that <paramref name="networkId"/> names.</summary>
    public void Talk(long networkId) => Send(new Interact(InteractKind.Npc, networkId, NpcOption.Talk));

    /// <summary>Goes on from the line in the open conversation.</summary>
    public void ContinueTalk() => Send(new Continue());

    /// <summary>Picks the option at <paramref name="index"/> of <see cref="Choices"/>.</summary>
    public void Choose(int index) => Send(new Choose(index));

    /// <summary>Asks the player <paramref name="networkId"/> names to trade. When they ask back, a trade opens and <see cref="PlayerTrade"/> is set.</summary>
    public void RequestTrade(long networkId) => Send(new Interact(InteractKind.Player, networkId, 0));

    /// <summary>Offers <paramref name="count"/> of <paramref name="item"/> in the open trade, in place of what was offered of it. A count of zero takes it back.</summary>
    public void Offer(int item, long count) => Send(new TradeOffer(item, count));

    /// <summary>Agrees to the open trade as it stands.</summary>
    public void AcceptTrade() => Send(new TradeAccept());

    /// <summary>Leaves the open trade.</summary>
    public void DeclineTrade() => Send(new TradeDecline());

    /// <summary>Buys <paramref name="count"/> of <paramref name="item"/> from the open shop.</summary>
    public void Buy(int item, long count) => Send(new Buy(item, count));

    /// <summary>Sells <paramref name="count"/> of <paramref name="item"/> to the open shop.</summary>
    public void Sell(int item, long count) => Send(new Sell(item, count));

    /// <summary>Puts up to <paramref name="count"/> of the inventory stack in <paramref name="slot"/> into bank <paramref name="tab"/>.</summary>
    public void Deposit(int slot, long count, int tab = 0) => Send(new Deposit(slot, count, tab));

    /// <summary>Takes up to <paramref name="count"/> of the bank stack in <paramref name="slot"/> into the inventory.</summary>
    public void Withdraw(int slot, long count) => Send(new Withdraw(slot, count));

    /// <summary>Uses the inventory item in <paramref name="slot"/> on the object <paramref name="networkId"/> names.</summary>
    public void UseItemOnObject(int slot, long networkId) => Send(new Interact(InteractKind.UseOnObject, networkId, slot));

    /// <summary>Uses the inventory item in <paramref name="slot"/> on the one in <paramref name="onSlot"/>.</summary>
    public void UseItemOnItem(int slot, int onSlot) => Send(new Interact(InteractKind.UseOnItem, onSlot, slot));

    /// <summary>
    /// Drags the stack in <paramref name="fromSlot"/> of <paramref name="from"/> onto <paramref name="toSlot"/> of
    /// <paramref name="to"/>. Nothing changes here. The slots change when the server sends them, so a move it
    /// refuses leaves the panel as it was.
    /// </summary>
    public void MoveItem(ContainerId from, int fromSlot, ContainerId to, int toSlot) => Send(new MoveItem(from, fromSlot, to, toSlot));

    /// <summary>Says <paramref name="text"/> to the players nearby. The text must be valid chat.</summary>
    public void Say(string text) => Send(new Chat(text));

    /// <summary>Sends <paramref name="text"/> privately to the player called <paramref name="to"/>. Both must be valid.</summary>
    public void Whisper(string to, string text) => Send(new Whisper(to, text));

    /// <summary>Puts <paramref name="name"/> on <paramref name="list"/>, or takes it off. The name must be a valid player name.</summary>
    public void EditList(ListKind list, string name, bool add) => Send(new EditList(list, add, name));

    /// <summary>
    /// Takes in what the server has sent, then blends the world towards where the server was a moment
    /// ago. Call once a frame.
    /// </summary>
    public void Advance()
    {
        while (_inbox.TryDequeue(out var message))
        {
            Apply(message);
        }

        _client.Advance();
        lock (_client.Gate)
        {
            _animator.Retain(_client.NetworkIds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.CloseAsync();
        _client.Dispose();
        _world.Dispose();
    }

    private static int Int(byte[] bytes, int index) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * sizeof(int)));

    private SkillDef Skill(string id) =>
        _skills.Find(id) ?? throw new ArgumentException($"{id} is not a skill.", nameof(id));

    private void Send(ClientMessage message) => _client.SendMessage(MessageCodec.Encode(message));

    private void Receive(byte[] body)
    {
        if (MessageCodec.DecodeServer(body) is Understood<ServerMessage> understood)
        {
            _inbox.Enqueue(understood.Message);
        }
    }

    // Copies, because the bytes belong to the world the link's thread writes.
    private byte[]? BytesOf(long networkId, ComponentType type)
    {
        lock (_client.Gate)
        {
            return _client.EntityFor((ulong)networkId) is { } entity && _world.TryGetBytes(entity, type, out var bytes) ? bytes.ToArray() : null;
        }
    }

    // shortcut: copies each entity's bytes on every call, fine for a view of a few hundred, take spans under the lock when a profile asks.
    private List<Seen> InView()
    {
        lock (_client.Gate)
        {
            var seen = new List<Seen>();
            foreach (var networkId in _client.NetworkIds)
            {
                if (_client.EntityFor(networkId) is { } entity && _world.TryGetBytes(entity, _position, out var at))
                {
                    seen.Add(new Seen((long)networkId, TilePosition.Read(at), Copy(entity, _ground), Copy(entity, _object), Copy(entity, _npc)));
                }
            }

            return seen;
        }
    }

    private byte[]? Copy(ulong entity, ComponentType type) => _world.TryGetBytes(entity, type, out var bytes) ? bytes.ToArray() : null;

    private void Apply(ServerMessage message)
    {
        switch (message)
        {
            case LoginOk ok:
                Name = ok.Name;
                break;
            case Avatar avatar:
                AvatarId = avatar.NetworkId;
                break;
            case PrayerView prayer:
                Prayer = prayer;
                break;
            case SocialLists lists:
                Friends = lists.Friends;
                Ignored = lists.Ignored;
                break;
            case PrivateState state:
                ApplyPrivate(state.Delta.Span);
                break;
            default:
                Hear(message);
                break;
        }
    }

    private void Hear(ServerMessage message)
    {
        switch (message)
        {
            case Notice notice:
                Keep(_notices, notice.Text);
                break;
            case Said said:
                _bubbles[said.Speaker] = (said.Text, Now + BubbleSeconds);
                Keep(_said, new Spoken(said.Speaker, said.Text));
                break;
            case Hit hit:
                Keep(_splats, new Splat(hit.Target, hit.Damage));
                break;
            case Projectile shot:
                Keep(_shots, new Shot(shot.From, shot.To, shot.Ticks, shot.Style));
                break;
            case Whispered whispered:
                Keep(_whispers, new Heard(whispered.From, whispered.Text));
                break;
            default:
                Open(message);
                break;
        }
    }

    private void Open(ServerMessage message)
    {
        switch (message)
        {
            case ShopView shop:
                CloseWindow();
                Shop = shop;
                break;
            case TradeView trade:
                CloseWindow();
                PlayerTrade = trade;
                break;
            case BankOpen:
                CloseWindow();
                IsBankOpen = true;
                break;
            case ConversationLine line:
                CloseWindow();
                Line = line;
                break;
            case ConversationChoices choices:
                CloseWindow();
                Choices = choices.Options;
                break;
            case WindowClosed:
                CloseWindow();
                break;
        }
    }

    private void CloseWindow()
    {
        Shop = null;
        PlayerTrade = null;
        IsBankOpen = false;
        Line = null;
        Choices = [];
    }

    private void ApplyPrivate(ReadOnlySpan<byte> delta)
    {
        if (delta.IsEmpty)
        {
            return;
        }

        switch (delta[0])
        {
            case SkillSet.DeltaTag:
                _skillXp = Applied(_skillXp, _skills.Schema, delta[1..]);
                break;
            case Story.DeltaTag when _story is not null && _questTable is not null:
                _questTable = Applied(_questTable, _story.Schema, delta[1..]);
                break;
            case ItemSet.DeltaTag when ContainerCodec.TryDecodeSlots(delta, _items) is { } slots:
                _containers[slots.Container].Mirror(slots.Changes);
                break;
        }
    }

    private static ProgressTable Applied(ProgressTable table, ProgressSchema schema, ReadOnlySpan<byte> bytes) =>
        Changes.TryDecode(schema, bytes) is { } changes ? table.Apply(changes) : table;

    private static void Keep<T>(List<T> lines, T line)
    {
        lines.Add(line);
        if (lines.Count > KeepLines)
        {
            lines.RemoveAt(0);
        }
    }

    private readonly record struct Seen(long NetworkId, TilePoint At, byte[]? Ground, byte[]? Object, byte[]? Npc);
}
