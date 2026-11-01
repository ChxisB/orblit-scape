using System.Net;
using System.Net.Sockets;
using System.Text;
using Orblit.Net;

namespace Scape.Server.Tests;

public class HealthTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<string> ReadAllAsync(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, Cancel);
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync(Cancel);
    }

    private static readonly PhaseSummary Summary = new()
    {
        Ticks = 100,
        TickAverage = TimeSpan.FromMicroseconds(2150),
        TickMax = TimeSpan.FromMicroseconds(7400),
        TickP95 = TimeSpan.Zero,
        DriftAverage = TimeSpan.Zero,
        DriftMax = TimeSpan.Zero,
        Phases = new Dictionary<string, PhaseStats>(),
        BytesSent = new Dictionary<string, ClientBytes>(),
    };

    [Fact]
    public void TheHealthLineNamesPlayersRegionsAndTheTick()
    {
        Assert.Equal(
            "Health: 3 players, 4 regions, tick average 2.1 ms, max 7.4 ms over 100 ticks",
            SummaryText.HealthLine(players: 3, regions: 4, Summary));
    }

    [Fact]
    public void ALateTickNamesTheWorstPhaseFirst()
    {
        var quick = TimeSpan.FromMilliseconds(1);
        var slow = TimeSpan.FromMilliseconds(9);
        var summary = Summary with
        {
            Phases = new Dictionary<string, PhaseStats>
            {
                ["inbound"] = new(quick, quick, quick),
                ["movement"] = new(slow, slow, slow),
            },
        };

        var lines = SummaryText.DescribeLateTick(summary).Split('\n');

        Assert.Equal(
            ["  movement: average 9.0 ms, max 9.0 ms", "  inbound: average 1.0 ms, max 1.0 ms"],
            lines[1..]);
    }

    [Fact]
    public async Task TheAlivePortAnswersAliveToEachConnection()
    {
        using var alive = AlivePort.Listen(IPAddress.Loopback, 0, new object());

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal("alive\n", await ReadAllAsync(alive.Port));
        }
    }

    [Fact]
    public async Task AMonitorThatHangsUpAtOnceDoesNotStopItAnswering()
    {
        using var alive = AlivePort.Listen(IPAddress.Loopback, 0, new object());

        using (var early = new TcpClient())
        {
            await early.ConnectAsync(IPAddress.Loopback, alive.Port, Cancel);
        }

        await Task.Delay(50, Cancel);

        Assert.Equal("alive\n", await ReadAllAsync(alive.Port));
    }
}
