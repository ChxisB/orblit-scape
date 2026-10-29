using System.Globalization;
using Orblit.Net;

namespace Scape.Server;

public static class SummaryText
{
    /// <summary>
    /// The line the server logs each minute: who is on, how big the world is and how the last
    /// ticks ran.
    /// </summary>
    public static string HealthLine(int players, int regions, PhaseSummary summary) =>
        Invariant($"Health: {players} players, {regions} regions, tick average {Ms(summary.TickAverage)}, max {Ms(summary.TickMax)} over {summary.Ticks} ticks");

    /// <summary>A late tick as lines of text, the worst phase of the window first.</summary>
    internal static string DescribeLateTick(PhaseSummary summary) => string.Join('\n', [
        Invariant($"late tick: average {Ms(summary.TickAverage)}, max {Ms(summary.TickMax)}, drift max {Ms(summary.DriftMax)}, over {summary.Ticks} ticks"),
        .. summary.Phases
            .OrderByDescending(phase => phase.Value.Max)
            .Select(phase => Invariant($"  {phase.Key}: average {Ms(phase.Value.Average)}, max {Ms(phase.Value.Max)}")),
    ]);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // Whole microseconds first, as the clock reports them, then rounded to a tenth of a millisecond.
    private static string Ms(TimeSpan span) =>
        Invariant($"{span.Ticks / TimeSpan.TicksPerMicrosecond / 1000.0:F1} ms");
}
