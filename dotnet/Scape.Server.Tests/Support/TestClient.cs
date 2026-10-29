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

    public TestClient(ScapeServer server)
    {
        var networkId = World.RegisterComponent("NetworkId", ComponentKind.Int64);
        Position = World.RegisterComponent(TilePosition.Name, ComponentKind.Float32, TilePosition.Floats);
        Client = new NetClient(new NetClientOptions
        {
            World = World,
            Set = new ReplicationSet([Position]),
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

    /// <summary>Whether the server has let this client in.</summary>
    public bool Admitted => Messages.Any(message => message is LoginOk);

    /// <summary>The tile the entity with <paramref name="networkId"/> stands on, as this client sees it.</summary>
    public TileCoord? TileOf(long networkId)
    {
        lock (Client.Gate)
        {
            if (Client.EntityFor((ulong)networkId) is not { } entity || !World.TryGetBytes(entity, Position, out var bytes))
            {
                return null;
            }

            return TilePosition.Nearest(TilePosition.Read(bytes));
        }
    }

    public Task LoginAsync(string name)
    {
        Client.Join(MessageCodec.Encode(new Login(name)));
        return Link.IdleAsync();
    }

    public Task SendAsync(ClientMessage message) => SendBytesAsync(MessageCodec.Encode(message));

    public Task SendBytesAsync(byte[] bytes)
    {
        Client.SendMessage(bytes);
        return Link.IdleAsync();
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
