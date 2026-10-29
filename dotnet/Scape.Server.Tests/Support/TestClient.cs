using System.Buffers.Binary;
using System.Collections.Concurrent;
using Orblit.Core;
using Orblit.Net;
using Scape.Core;

namespace Scape.Server.Tests;

/// <summary>A client on a loopback link to a server, reading what it is sent.</summary>
internal sealed class TestClient : IAsyncDisposable
{
    // Messages arrive on the link's pool thread while a test reads them.
    private readonly ConcurrentQueue<Decoded<ServerMessage>> _received = [];
    private readonly ConcurrentQueue<string> _goodbyes = [];
    private readonly ComponentType _ground;
    private readonly ComponentType _object;
    private readonly ComponentType _npc;
    private readonly ComponentType _health;
    private readonly ComponentType _look;
    private readonly ComponentType _anim;

    public TestClient(ScapeServer server)
    {
        var networkId = World.RegisterComponent("NetworkId", ComponentKind.Int64);
        Position = World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        _ground = World.RegisterComponent(GroundItem.Name, ComponentKind.Int32, GroundItem.Ints);
        _object = World.RegisterComponent(WorldObject.Name, ComponentKind.Int32, WorldObject.Ints);
        _npc = World.RegisterComponent(Npc.Name, ComponentKind.Int32, Npc.Ints);
        _health = World.RegisterComponent(Health.Name, ComponentKind.Int32, Health.Ints);
        _look = World.RegisterComponent(Look.Name, ComponentKind.Int32, Look.Ints);
        _anim = World.RegisterComponent(Anim.Name, ComponentKind.Int32, Anim.Ints);
        Client = new NetClient(new NetClientOptions
        {
            World = World,
            Set = new ReplicationSet([Position, _ground, _object, _npc, _health, _look, _anim]),
            NetworkId = networkId,
            Transport = Link.Client,
            OnMessage = body => _received.Enqueue(MessageCodec.DecodeServer(body)),
            OnGoodbye = _goodbyes.Enqueue,
        });
        server.Host.Accept(Link.Host);
    }

    public World World { get; } = new();

    public LoopbackLink Link { get; } = new();

    public ComponentType Position { get; }

    public NetClient Client { get; }

    public IReadOnlyList<string> Goodbyes => [.. _goodbyes];

    /// <summary>The messages that were understood.</summary>
    public IReadOnlyList<ServerMessage> Messages =>
        [.. _received.OfType<Understood<ServerMessage>>().Select(understood => understood.Message)];

    /// <summary>The text of each notice, oldest first.</summary>
    public IReadOnlyList<string> Notices => [.. Messages.OfType<Notice>().Select(notice => notice.Text)];

    /// <summary>Every hit this client has been shown, oldest first.</summary>
    public IReadOnlyList<Hit> Hits => [.. Messages.OfType<Hit>()];

    /// <summary>The network id of this client's own avatar.</summary>
    public long Avatar => Messages.OfType<Avatar>().Single().NetworkId;

    /// <summary>Whether the server has let this client in.</summary>
    public bool Admitted => Messages.Any(message => message is LoginOk);

    /// <summary>The items on the ground this client has been told about, as (item, count) by network id.</summary>
    public Dictionary<long, (int Item, int Count)> GroundItems => All(_ground).ToDictionary(p => p.Key, p => Pair(p.Value));

    /// <summary>The objects this client has been told about, as (definition, state) by network id.</summary>
    public Dictionary<long, (int Def, int State)> Objects => All(_object).ToDictionary(p => p.Key, p => Pair(p.Value));

    /// <summary>The creatures this client has been told about, as their definition id by network id.</summary>
    public Dictionary<long, int> Npcs => All(_npc).ToDictionary(p => p.Key, p => BinaryPrimitives.ReadInt32LittleEndian(p.Value));

    /// <summary>The tile the entity with <paramref name="networkId"/> stands on, as this client sees it.</summary>
    public TileCoord? TileOf(long networkId) =>
        BytesOf(networkId, Position) is { } bytes ? TilePosition.Nearest(TilePosition.Read(bytes)) : null;

    /// <summary>The hit points of the entity with <paramref name="networkId"/> as this client sees them, or null when it has none.</summary>
    public (int Now, int Max)? HealthOf(long networkId) => BytesOf(networkId, _health) is { } bytes ? Pair(bytes) : null;

    /// <summary>How the entity with <paramref name="networkId"/> looks as this client sees it, or null.</summary>
    public Look? LookOf(long networkId) => BytesOf(networkId, _look) is { } bytes ? Look.TryRead(bytes) : null;

    /// <summary>What the entity with <paramref name="networkId"/> is doing as this client sees it, or null.</summary>
    public Anim? AnimOf(long networkId) => BytesOf(networkId, _anim) is { } bytes ? Anim.TryRead(bytes) : null;

    public Task LoginAsync(string name)
    {
        Client.Join(MessageCodec.Encode(new Login(name)));
        return Link.IdleAsync();
    }

    public Task SendAsync(ClientMessage message) => SendBytesAsync(MessageCodec.Encode(message));

    /// <summary>Sends a click of <paramref name="kind"/> on <paramref name="id"/>, as <see cref="InteractKind"/> says.</summary>
    public Task ClickAsync(int kind, long id, int option = 0) => SendAsync(new Interact(kind, id, option));

    /// <summary>Sends a click to attack the creature <paramref name="networkId"/>.</summary>
    public Task AttackAsync(long networkId) => ClickAsync(InteractKind.Npc, networkId, NpcOption.Attack);

    public Task SendBytesAsync(byte[] bytes)
    {
        Client.SendMessage(bytes);
        return Link.IdleAsync();
    }

    private static (int, int) Pair(byte[] bytes) =>
        (BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sizeof(int))));

    // Copies, because the bytes belong to the world the link's thread writes.
    private byte[]? BytesOf(long networkId, ComponentType type)
    {
        lock (Client.Gate)
        {
            return Client.EntityFor((ulong)networkId) is { } entity && World.TryGetBytes(entity, type, out var bytes) ? bytes.ToArray() : null;
        }
    }

    private Dictionary<long, byte[]> All(ComponentType type)
    {
        lock (Client.Gate)
        {
            var found = new Dictionary<long, byte[]>();
            foreach (var networkId in Client.NetworkIds)
            {
                if (Client.EntityFor(networkId) is { } entity && World.TryGetBytes(entity, type, out var bytes))
                {
                    found[(long)networkId] = bytes.ToArray();
                }
            }

            return found;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Link.CloseAsync();
        // A close is delivered like a message, so the host has not heard of it until the link is idle.
        await Link.IdleAsync();
        Client.Dispose();
        World.Dispose();
    }
}
