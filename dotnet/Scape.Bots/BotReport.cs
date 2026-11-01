using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Scape.Core;

namespace Scape.Bots;

/// <summary>Where a run happened. A tick time means nothing without it.</summary>
public sealed record Machine(string Os, string OsVersion, int Processors, string Runtime)
{
    /// <summary>The machine this process runs on. F10: a report names the real machine.</summary>
    public static Machine Current() => new(
        OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsWindows() ? "windows" : "other",
        RuntimeInformation.OSDescription,
        Environment.ProcessorCount,
        RuntimeInformation.FrameworkDescription);
}

/// <summary>
/// What one run of bots found. The tick times are the server's own, taken round the server's tick,
/// so they leave out what the bots spent.
/// </summary>
public sealed class BotReport
{
    // What BotRun.ServerOn leaves out of the server. A report must name each one, so keep the two in step.
    private static readonly string[] Without = ["storage", "accounts", "recipes", "spells", "prayers"];

    private IReadOnlyList<TimeSpan>? _sorted;

    public required Machine Machine { get; init; }

    public required int Bots { get; init; }

    /// <summary>How many of the bots were in the world at the end.</summary>
    public required int Joined { get; init; }

    public required int Seed { get; init; }

    public required IReadOnlyList<TimeSpan> TickTimes { get; init; }

    /// <summary>Lines the server logged, and bots that were sent away.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    public TimeSpan P95 => Percentile(Sorted, 0.95);

    public TimeSpan Slowest => Sorted.Count == 0 ? TimeSpan.Zero : Sorted[^1];

    /// <summary>Whether every bot got in, nothing went wrong, and the p95 tick fit in the tick period.</summary>
    public bool Healthy => Joined == Bots && Problems.Count == 0 && P95 <= GameTick.Period;

    /// <summary>0 when <see cref="Healthy"/>, 1 when not.</summary>
    public int ExitCode => Healthy ? 0 : 1;

    internal static int PeriodMs => (int)GameTick.Period.TotalMilliseconds;

    private IReadOnlyList<TimeSpan> Sorted => _sorted ??= [.. TickTimes.Order()];

    /// <summary>The tick time <paramref name="p"/> of the way up <paramref name="sorted"/>, from 0 to 1, by nearest rank. Zero when there are no ticks.</summary>
    public static TimeSpan Percentile(IReadOnlyList<TimeSpan> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count), 1, sorted.Count) - 1];
    }

    /// <summary>A tick time as text, in milliseconds.</summary>
    public static string FormatMs(TimeSpan time) => FormattableString.Invariant($"{time.TotalMilliseconds:F3} ms");

    public JsonObject ToJson()
    {
        var sorted = Sorted;
        var total = TickTimes.Aggregate(TimeSpan.Zero, (sum, time) => sum + time);
        return new JsonObject
        {
            ["machine"] = new JsonObject
            {
                ["os"] = Machine.Os,
                ["osVersion"] = Machine.OsVersion,
                ["processors"] = Machine.Processors,
                ["runtime"] = Machine.Runtime,
            },
            ["transport"] = "loopback",
            ["without"] = new JsonArray([.. Without.Select(name => (JsonNode?)name)]),
            ["bots"] = Bots,
            ["joined"] = Joined,
            ["seed"] = Seed,
            ["ticks"] = TickTimes.Count,
            ["tickMs"] = new JsonObject
            {
                ["average"] = (TickTimes.Count == 0 ? TimeSpan.Zero : total / TickTimes.Count).TotalMilliseconds,
                ["p50"] = Percentile(sorted, 0.5).TotalMilliseconds,
                ["p95"] = P95.TotalMilliseconds,
                ["max"] = Slowest.TotalMilliseconds,
            },
            ["periodMs"] = PeriodMs,
            ["healthy"] = Healthy,
            ["problems"] = new JsonArray([.. Problems.Select(problem => (JsonNode?)problem)]),
        };
    }
}
