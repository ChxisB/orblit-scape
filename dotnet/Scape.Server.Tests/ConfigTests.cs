namespace Scape.Server.Tests;

public class ConfigTests
{
    [Fact]
    public void EverySettingHasADefault()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string>());

        Assert.Equal(7600, config.Port);
        Assert.Equal("127.0.0.1", config.Address);
        Assert.Equal("scape-saves", config.Data);
        Assert.Equal("scape", config.World);
        Assert.Equal("scape-saves/scape", config.Saves);
        Assert.Equal(7601, config.HealthPort);
        Assert.Equal(LogLevel.Info, config.LogLevel);
        Assert.Empty(config.Roles);
    }

    [Fact]
    public void ReadsEachSettingFromTheEnvironment()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string>
        {
            ["SCAPE_PORT"] = "8000",
            ["SCAPE_ADDRESS"] = "0.0.0.0",
            ["SCAPE_DATA"] = "/var/lib/scape",
            ["SCAPE_WORLD"] = "island-2",
            ["SCAPE_HEALTH_PORT"] = "0",
            ["SCAPE_LOG_LEVEL"] = "warn",
            ["SCAPE_ADMINS"] = "ana=admin,bo=moderator",
        });

        Assert.Equal(8000, config.Port);
        Assert.Equal("0.0.0.0", config.Address);
        Assert.Equal("/var/lib/scape/island-2", config.Saves);
        Assert.Equal(0, config.HealthPort);
        Assert.Equal(LogLevel.Warn, config.LogLevel);
        Assert.Equal(new Dictionary<string, Role> { ["ana"] = Role.Admin, ["bo"] = Role.Moderator }, config.Roles);
    }

    [Fact]
    public void ReadsInfoAsALogLevel()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string> { ["SCAPE_LOG_LEVEL"] = "info" });

        Assert.Equal(LogLevel.Info, config.LogLevel);
    }

    [Fact]
    public void AnEmptyValueCountsAsUnset()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string>
        {
            ["SCAPE_PORT"] = "",
            ["SCAPE_WORLD"] = "",
            ["SCAPE_ADMINS"] = "",
        });

        Assert.Equal(7600, config.Port);
        Assert.Equal("scape", config.World);
        Assert.Empty(config.Roles);
    }

    [Theory]
    [InlineData("SCAPE_PORT", "0")]
    [InlineData("SCAPE_PORT", "65536")]
    [InlineData("SCAPE_PORT", "seven")]
    [InlineData("SCAPE_HEALTH_PORT", "-1")]
    [InlineData("SCAPE_HEALTH_PORT", "70000")]
    [InlineData("SCAPE_WORLD", "Island")]
    [InlineData("SCAPE_WORLD", "../etc")]
    [InlineData("SCAPE_WORLD", "-lead")]
    [InlineData("SCAPE_WORLD", "scape\n")]
    [InlineData("SCAPE_WORLD", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("SCAPE_LOG_LEVEL", "debug")]
    [InlineData("SCAPE_ADMINS", "ana")]
    [InlineData("SCAPE_ADMINS", "ana=owner")]
    [InlineData("SCAPE_ADMINS", "ana=admin=x")]
    [InlineData("SCAPE_ADMINS", "ana!=admin")]
    [InlineData("SCAPE_ADMINS", "abcdefghijklm=admin")]
    [InlineData("SCAPE_ADMINS", "ana=admin,Ana=moderator")]
    [InlineData("SCAPE_PORT", "7601")]
    [InlineData("SCAPE_HEALTH_PORT", "7600")]
    public void StopsStartupForABadValue(string name, string value)
    {
        var error = Assert.Throws<FormatException>(() => ServerConfig.FromEnvironment(new Dictionary<string, string> { [name] = value }));

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TakesTheLongestWorldAndTheTopPort()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string>
        {
            ["SCAPE_WORLD"] = new string('a', 32),
            ["SCAPE_PORT"] = "65535",
            ["SCAPE_HEALTH_PORT"] = "65534",
        });

        Assert.Equal(32, config.World.Length);
        Assert.Equal(65535, config.Port);
        Assert.Equal(65534, config.HealthPort);
    }

    [Fact]
    public void ReadsAdminNamesWithSpacesAndCapitalsAsLowerCaseKeys()
    {
        var config = ServerConfig.FromEnvironment(new Dictionary<string, string>
        {
            ["SCAPE_ADMINS"] = "Ana = admin, bo=moderator",
        });

        Assert.Equal(["ana", "bo"], config.Roles.Keys);
        Assert.Equal(Role.Admin, config.Roles["ana"]);
    }
}
