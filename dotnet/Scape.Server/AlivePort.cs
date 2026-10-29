using System.Net;
using System.Net.Sockets;

namespace Scape.Server;

/// <summary>Answers every connection with <c>alive</c> and closes it.</summary>
/// <remarks>
/// Each answer waits for the gate a tick holds, so a stalled tick stalls the answer, and a monitor
/// sees that as a dead server.
/// </remarks>
public sealed class AlivePort : IDisposable
{
    private readonly TcpListener _listener;
    private readonly object _gate;

    private AlivePort(TcpListener listener, object gate)
    {
        _listener = listener;
        _gate = gate;
    }

    /// <summary>The port it listens on, which the system chose when it was asked for port zero.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static AlivePort Listen(IPAddress address, int port, object gate)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        var alive = new AlivePort(listener, gate);
        _ = alive.AnswerAllAsync();
        return alive;
    }

    public void Dispose() => _listener.Dispose();

    private async Task AnswerAllAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptSocketAsync();
            }
            catch (Exception error) when (error is ObjectDisposedException or SocketException)
            {
                return;
            }

            Answer(client);
        }
    }

    private void Answer(Socket client)
    {
        using (client)
        {
            lock (_gate)
            {
                // Nothing to do inside. Taking the gate is the wait for the tick.
            }

            try
            {
                client.Send("alive\n"u8);
                client.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
                // A monitor that hangs up early must not take the server down.
            }
        }
    }
}
