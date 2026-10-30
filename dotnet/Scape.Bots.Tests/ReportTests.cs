using System.Text.Json.Nodes;
using Scape.Core;

namespace Scape.Bots.Tests;

public sealed class ReportTests
{
    private static readonly Machine TestMachine = new("testos", "1", 4, "3.0.0");

    private static TimeSpan[] Ms(IEnumerable<int> values) => [.. values.Select(value => TimeSpan.FromMilliseconds(value))];

    private static BotReport Report(int bots = 2, int joined = 2, TimeSpan[]? ticks = null, string[]? problems = null) => new()
    {
        Machine = TestMachine,
        Bots = bots,
        Joined = joined,
        Seed = 7,
        TickTimes = ticks ?? Ms([1, 2, 3]),
        Problems = problems ?? [],
    };

    [Fact]
    public void PercentileTakesTheNearestRank()
    {
        var sorted = Ms(Enumerable.Range(1, 20));

        Assert.Equal(TimeSpan.FromMilliseconds(19), BotReport.Percentile(sorted, 0.95));
        Assert.Equal(TimeSpan.FromMilliseconds(10), BotReport.Percentile(sorted, 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(20), BotReport.Percentile(sorted, 1));
    }

    [Fact]
    public void PercentileRoundsUpToTheNextRank() =>
        Assert.Equal(TimeSpan.FromMilliseconds(3), BotReport.Percentile(Ms([1, 2, 3]), 0.95));

    [Fact]
    public void PercentileIsZeroWhenThereAreNoTicks() =>
        Assert.Equal(TimeSpan.Zero, BotReport.Percentile([], 0.95));

    [Fact]
    public void WritesTheMachineTheBotCountAndTheP95()
    {
        var json = Report(ticks: Ms(Enumerable.Range(1, 20).Reverse())).ToJson();

        var machine = JsonNode.Parse("""{ "os": "testos", "osVersion": "1", "processors": 4, "runtime": "3.0.0" }""");
        Assert.True(JsonNode.DeepEquals(machine, json["machine"]));
        Assert.Equal(2, (int?)json["bots"]);
        Assert.Equal(7, (int?)json["seed"]);
        Assert.Equal(20, (int?)json["ticks"]);
        var tickMs = JsonNode.Parse("""{ "average": 10.5, "p50": 10, "p95": 19, "max": 20 }""");
        Assert.True(JsonNode.DeepEquals(tickMs, json["tickMs"]));
    }

    [Fact]
    public void WritesWhatTheRunLeftOutAndTheVerdict()
    {
        var json = Report(problems: ["late"]).ToJson();

        Assert.Equal("loopback", (string?)json["transport"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""["storage", "accounts", "recipes", "spells", "prayers"]"""), json["without"]));
        Assert.Equal(600, (int?)json["periodMs"]);
        Assert.False((bool?)json["healthy"]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""["late"]"""), json["problems"]));
    }

    [Fact]
    public void FormatsATickTimeInMilliseconds()
    {
        Assert.Equal("2.500 ms", BotReport.FormatMs(TimeSpan.FromMilliseconds(2.5)));
        Assert.Equal("0.000 ms", BotReport.FormatMs(TimeSpan.Zero));
    }

    [Fact]
    public void IsHealthyWhenTheP95TickIsExactlyThePeriod() => Assert.True(Report(ticks: [GameTick.Period]).Healthy);

    [Fact]
    public void IsHealthyWhenEveryBotIsInAndNothingWentWrong() => Assert.True(Report().Healthy);

    [Fact]
    public void IsNotHealthyWhenABotDidNotGetIn() => Assert.False(Report(joined: 1).Healthy);

    [Fact]
    public void IsNotHealthyWhenTheServerLoggedAProblem() => Assert.False(Report(problems: ["late"]).Healthy);

    [Fact]
    public void IsNotHealthyWhenTheP95TickIsOverThePeriod()
    {
        TimeSpan[] slow = [GameTick.Period + TimeSpan.FromMilliseconds(1)];

        Assert.False(Report(ticks: slow).Healthy);
    }

    [Fact]
    public void ReadsZeroForARunWithNoTicks()
    {
        var report = Report(ticks: []);

        Assert.Equal(TimeSpan.Zero, report.P95);
        Assert.Equal(TimeSpan.Zero, report.Slowest);
    }

    [Fact]
    public void ExitsZeroWhenHealthyAndOneWhenNot()
    {
        Assert.Equal(0, Report().ExitCode);
        Assert.Equal(1, Report(joined: 1).ExitCode);
    }
}
