using System.Text.Json;
using Orblit.Net;

namespace Scape.Server;

/// <summary>Waits until the next tick is due. Declared here so a test can move time by hand (F10).</summary>
public interface IPacer
{
    Task UntilNextAsync();
}

/// <summary>Waits on a <see cref="TickClock"/>.</summary>
public sealed class ClockPacer(TickClock clock) : IPacer
{
    public Task UntilNextAsync() => Task.Delay(clock.UntilNext());
}

/// <summary>Runs the world one tick at a time.</summary>
/// <remarks>
/// A tick holds the host's gate. Clients arrive and send on other threads, and the host takes the
/// same gate for each of them, so none of it lands in the middle of a tick.
/// </remarks>
public sealed class TickLoop(NetHost host, Registry registry, IPacer pacer)
{
    private static readonly (Phase Phase, string Name)[] Phases =
        [.. Enum.GetValues<Phase>().Select(phase => (phase, JsonNamingPolicy.CamelCase.ConvertName(phase.ToString())))];

    private volatile bool _stopped;

    public NetHost Host { get; } = host;

    public Registry Registry { get; } = registry;

    public long Tick => Registry.Actions.Tick;

    /// <summary>Ticks until <see cref="Stop"/> is called.</summary>
    public async Task RunAsync()
    {
        while (!_stopped)
        {
            await pacer.UntilNextAsync();
            if (_stopped)
            {
                return;
            }

            RunTick();
        }
    }

    public void Stop() => _stopped = true;

    public void RunTick()
    {
        lock (Host.Gate)
        {
            Registry.Actions.Advance();
            foreach (var (phase, name) in Phases)
            {
                Host.Phase(name, () => RunPhase(phase));
            }

            Host.Publish();
        }
    }

    private void RunPhase(Phase phase)
    {
        if (phase == Phase.Actions)
        {
            Registry.Actions.RunDue();
        }

        foreach (var system in Registry.SystemsFor(phase))
        {
            system();
        }
    }
}
