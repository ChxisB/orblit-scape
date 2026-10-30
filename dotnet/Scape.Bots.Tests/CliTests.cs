using System.Text.Json.Nodes;

namespace Scape.Bots.Tests;

public sealed class CliTests
{
    public static TheoryData<string[]> BadArguments => new()
    {
        { ["--bots", "0"] },
        { ["--bots", "10000"] },
        { ["--bots", "many"] },
        { ["--ticks", "-1"] },
        { ["--reach", "1001"] },
        { ["--bots"] },
        { ["--speed", "3"] },
        { ["--ticks", "0"] },
        { ["--ticks", "1000001"] },
        { ["--seed", "-1"] },
        { ["--seed", "1073741825"] },
        { ["--reach", "-1"] },
        { ["--bots", "--ticks", "5"] },
        { ["--out", "--seed", "1"] },
        { ["--out", "--seed"] },
    };

    public static TheoryData<string[]> EdgeArguments => new()
    {
        { ["--seed", "0"] },
        { ["--seed", "1073741824"] },
        { ["--reach", "0"] },
        { ["--reach", "1000"] },
    };

    private readonly List<string> _lines = [];
    private readonly Dictionary<string, string> _saved = [];

    private Task<int> RunAsync(params string[] args) =>
        BotsCli.RunAsync(args, _lines.Add, (path, text) => _saved[path] = text);

    private JsonNode? Saved(string path) => JsonNode.Parse(_saved[path]);

    [Fact]
    public async Task RunsSavesTheResultAndExitsZero()
    {
        var code = await RunAsync("--bots", "3", "--ticks", "5", "--out", "r.json");

        Assert.Equal(0, code);
        var json = Saved("r.json");
        Assert.Equal(3, json?["bots"]?.GetValue<int>());
        Assert.Equal(3, json?["joined"]?.GetValue<int>());
        Assert.Equal(5, json?["ticks"]?.GetValue<int>());
        Assert.StartsWith("3 bots, 5 ticks. p95 ", _lines[0]);
        Assert.Equal("Result in r.json.", _lines[^1]);
    }

    [Fact]
    public async Task PrintsTheResultAndExitsOneWhenItCannotBeSaved()
    {
        var code = await BotsCli.RunAsync(
            ["--bots", "2", "--ticks", "3", "--out", "r.json"],
            _lines.Add,
            (_, _) => throw new IOException("disk full"));

        Assert.Equal(1, code);
        Assert.Contains("Could not save r.json", _lines[0]);
        Assert.Contains("\"bots\": 2", _lines[0]);
        Assert.False(_lines[^1].StartsWith("Result in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AlsoExitsOneWhenTheFolderCannotBeWrittenTo()
    {
        var code = await BotsCli.RunAsync(
            ["--bots", "1", "--ticks", "1"],
            _lines.Add,
            (_, _) => throw new UnauthorizedAccessException("no"));

        Assert.Equal(1, code);
        Assert.Contains("Could not save bots-result.json: no", _lines[0]);
    }

    [Fact]
    public async Task ExitsOneWhenThePathIsNotAFileName()
    {
        foreach (Exception error in new Exception[] { new ArgumentException("empty"), new NotSupportedException("colon") })
        {
            _lines.Clear();

            var code = await BotsCli.RunAsync(["--bots", "1", "--ticks", "1"], _lines.Add, (_, _) => throw error);

            Assert.Equal(1, code);
            Assert.Contains($"Could not save bots-result.json: {error.Message}", _lines[0], StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(EdgeArguments))]
    public async Task ReadsTheLowestAndHighestValueEachOptionTakes(string[] args)
    {
        Assert.Equal(0, await RunAsync([.. args, "--bots", "1", "--ticks", "1"]));

        Assert.Single(_saved);
    }

    [Fact]
    public async Task DefaultsToFiftyBotsAndAHundredTicksWrittenToBotsResultJson()
    {
        Assert.Equal(0, await RunAsync());

        var json = Saved("bots-result.json");
        Assert.Equal(50, json?["bots"]?.GetValue<int>());
        Assert.Equal(100, json?["ticks"]?.GetValue<int>());
        Assert.Equal(1, json?["seed"]?.GetValue<int>());
    }

    [Fact]
    public void SummarisesAHealthyRunInOneLine()
    {
        BotsCli.Summarise(Report(joined: 2, problems: []), 3, _lines.Add);

        Assert.Equal("2 bots, 3 ticks. p95 3.000 ms, max 3.000 ms, period 600 ms.", Assert.Single(_lines));
    }

    [Fact]
    public void SummarisesAnUnhealthyRunWithItsFirstFiveProblems()
    {
        BotsCli.Summarise(Report(joined: 1, problems: ["a", "b", "c", "d", "e", "f"]), 3, _lines.Add);

        Assert.Equal(7, _lines.Count);
        Assert.Equal("Not healthy: 1 of 2 bots joined, 6 problems.", _lines[1]);
        Assert.Equal(["a", "b", "c", "d", "e"], _lines[2..]);
    }

    private static BotReport Report(int joined, string[] problems) => new()
    {
        Machine = new Machine("testos", "1", 4, "3.0.0"),
        Bots = 2,
        Joined = joined,
        Seed = 1,
        TickTimes = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(3)],
        Problems = problems,
    };

    [Theory]
    [MemberData(nameof(BadArguments))]
    public async Task ExitsTwoAndSavesNothingForArgumentsItCannotRead(string[] args)
    {
        Assert.Equal(2, await RunAsync(args));

        Assert.Empty(_saved);
        Assert.EndsWith(BotsCli.Usage, Assert.Single(_lines));
    }
}
