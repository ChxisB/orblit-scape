using System.Globalization;
using System.Text.Json;
using Scape.Core;

namespace Scape.Bots;

/// <summary>The command line of the bots program.</summary>
public static class BotsCli
{
    public const string Usage = """
        Usage: dotnet run --project Scape.Bots -- [options]
          --bots N    bots to log in, 1 to 9999, default 50
          --ticks N   ticks to run, default 100
          --seed N    seed for what the bots do, default 1
          --reach N   tiles from the spawn a bot may walk, default 12
          --out PATH  where to write the result, default bots-result.json
        """;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Runs the bots command: starts a server, logs in bots, steps ticks and saves the result as
    /// JSON. The exit code is 0 when every bot got in, the server logged nothing and the p95 tick fit
    /// in the tick period, 1 when not, and 2 for arguments it cannot read.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, Action<string> writeLine, Action<string, string> save)
    {
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (FormatException error)
        {
            writeLine($"{error.Message}\n{Usage}");
            return 2;
        }

        await using var run = await BotRun.StartAsync(options.Bots, options.Seed, options.Reach);
        await run.StepAsync(options.Ticks);
        var report = run.Report();
        var json = report.ToJson().ToJsonString(Indented) + "\n";
        var saved = true;
        try
        {
            save(options.Out, json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            saved = false;
            writeLine($"Could not save {options.Out}: {error.Message}\n{json}");
        }

        Summarise(report, options.Ticks, writeLine);
        if (saved)
        {
            writeLine($"Result in {options.Out}.");
        }

        return saved ? report.ExitCode : 1;
    }

    internal static void Summarise(BotReport report, int ticks, Action<string> writeLine)
    {
        writeLine(
            $"{report.Bots} bots, {ticks} ticks. p95 {BotReport.FormatMs(report.P95)}, "
            + $"max {BotReport.FormatMs(report.Slowest)}, period {BotReport.PeriodMs} ms.");
        if (report.Healthy)
        {
            return;
        }

        writeLine($"Not healthy: {report.Joined} of {report.Bots} bots joined, {report.Problems.Count} problems.");
        foreach (var problem in report.Problems.Take(5))
        {
            writeLine(problem);
        }
    }

    private static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>
        {
            ["--bots"] = "50",
            ["--ticks"] = "100",
            ["--seed"] = "1",
            ["--reach"] = "12",
            ["--out"] = "bots-result.json",
        };
        for (var i = 0; i < args.Length; i += 2)
        {
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            if (!values.ContainsKey(args[i]) || !hasValue)
            {
                throw new FormatException($"{args[i]} is not an option with a value.");
            }

            values[args[i]] = args[i + 1];
        }

        return new Options(
            Number("--bots", values["--bots"], 1, 9999),
            Number("--ticks", values["--ticks"], 1, 1_000_000),
            Number("--seed", values["--seed"], 0, 1 << 30),
            Number("--reach", values["--reach"], 0, 1000),
            values["--out"]);
    }

    private static int Number(string flag, string text, int least, int most) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value >= least && value <= most
            ? value
            : throw new FormatException($"{flag} wants a whole number from {least} to {most}.");

    private sealed record Options(int Bots, int Ticks, int Seed, int Reach, string Out);
}
