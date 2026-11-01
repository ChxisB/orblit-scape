using Orblit.Progress;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class SkillsTests : IAsyncDisposable
{
    private static readonly XpCurve Curve = DefaultSkills.All.Curve;

    private readonly TestServer _test = new();

    private Skills Skills => _test.Server.Skills;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static int SlotOf(string skill) => DefaultSkills.All.Schema.PlaceOf(SkillSet.XpName(skill)).Slot;

    [Fact]
    public async Task ExperienceOfListsOnlyTheSkillsAboveZero()
    {
        await _test.JoinAsync("Ana");
        Skills.Grant("ana", "mining", 40);
        Skills.Grant("ana", "fishing", 0);

        Assert.Equal(new Dictionary<string, long> { ["mining"] = 40 }, Skills.ExperienceOf("ana"));
        Assert.Empty(Skills.ExperienceOf("nobody"));
    }

    [Fact]
    public async Task APlayerWhoIsGoneHasNoExperienceHeld()
    {
        var ana = await _test.JoinAsync("Ana");
        Skills.Grant("ana", "mining", 40);

        await _test.LeaveAsync(ana);
        await _test.TickAsync(12);

        Assert.Equal(0, Skills.XpOf("ana", "mining"));
    }

    // Restore.

    [Fact]
    public async Task RestoreSetsTheExperienceAndTellsTheOwnerWhatIsAboveZero()
    {
        var ana = await _test.JoinAsync("Ana");

        var dropped = Skills.Restore("ana", new Dictionary<string, long> { ["mining"] = 70, ["fishing"] = 5 });
        await _test.TickAsync();

        Assert.Equal(0, dropped);
        Assert.Equal(70, Skills.XpOf("ana", "mining"));
        Assert.Equal(5, Skills.XpOf("ana", "fishing"));
        Assert.Equal(70, ana.SkillDeltas[^1].ValueOf(SlotOf("mining")));
    }

    [Fact]
    public async Task RestoreCountsASkillTheGameNoLongerHas()
    {
        await _test.JoinAsync("Ana");

        var dropped = Skills.Restore("ana", new Dictionary<string, long> { ["mining"] = 70, ["gone"] = 9 });

        Assert.Equal(1, dropped);
        Assert.Equal(70, Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task RestoreStopsExperienceAtTheCap()
    {
        await _test.JoinAsync("Ana");

        Skills.Restore("ana", new Dictionary<string, long> { ["mining"] = 0xFFFFFFFF });

        Assert.Equal(Curve.XpForLevel(50), Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public void RestoreForAPlayerWhoIsNotHereDropsTheLot() =>
        Assert.Equal(1, Skills.Restore("nobody", new Dictionary<string, long> { ["mining"] = 70 }));

    // Grants.

    [Fact]
    public async Task ANewPlayerStartsAtLevelOneWithNoExperience()
    {
        await _test.JoinAsync("Ana");

        Assert.Equal(0, Skills.XpOf("ana", "mining"));
        Assert.Equal(1, Skills.LevelOf("ana", "mining"));
    }

    [Fact]
    public async Task AGrantThatCrossesThreeLevelsReturnsEachOnce()
    {
        await _test.JoinAsync("Ana");

        var levels = Skills.Grant("ana", "mining", Curve.XpForLevel(4));

        Assert.Equal([2, 3, 4], levels);
        Assert.Equal(4, Skills.LevelOf("ana", "mining"));
    }

    [Fact]
    public async Task AGrantShortOfTheNextLevelReturnsNothing()
    {
        await _test.JoinAsync("Ana");

        Assert.Empty(Skills.Grant("ana", "mining", Curve.XpForLevel(2) - 1));
        Assert.Equal([2], Skills.Grant("ana", "mining", 1));
        Assert.Empty(Skills.Grant("ana", "mining", 0));
    }

    [Fact]
    public async Task ExperienceStopsAtTheCapAndSoDoTheLevels()
    {
        await _test.JoinAsync("Ana");

        var levels = Skills.Grant("ana", "mining", 0xFFFFFFFF);

        Assert.Equal(Enumerable.Range(2, 49), levels);
        Assert.Equal(Curve.XpForLevel(50), Skills.XpOf("ana", "mining"));
        Assert.Empty(Skills.Grant("ana", "mining", 1000));
    }

    [Fact]
    public async Task AGrantNearTheLongLimitStopsAtTheCap()
    {
        await _test.JoinAsync("Ana");
        Skills.Grant("ana", "mining", 10);

        Skills.Grant("ana", "mining", long.MaxValue);

        Assert.Equal(Curve.XpForLevel(50), Skills.XpOf("ana", "mining"));
    }

    [Fact]
    public async Task TheOwnerIsSentOnlyTheSkillThatChanged()
    {
        var ana = await _test.JoinAsync("Ana");
        await _test.JoinAsync("Bo");
        Skills.Grant("ana", "fishing", 100);
        await _test.TickAsync();

        var delta = Assert.Single(ana.SkillDeltas);

        Assert.Equal([SlotOf("fishing")], delta.Slots);
        Assert.Equal(100, delta.ValueOf(SlotOf("fishing")));
    }

    [Fact]
    public async Task NoOneElseIsSentAnything()
    {
        await _test.JoinAsync("Ana");
        var bo = await _test.JoinAsync("Bo");

        Skills.Grant("ana", "fishing", 100);
        await _test.TickAsync();

        Assert.Empty(bo.SkillDeltas);
    }

    [Fact]
    public async Task AGrantThatChangesNothingSendsNothing()
    {
        var ana = await _test.JoinAsync("Ana");

        Skills.Grant("ana", "fishing", 0);
        await _test.TickAsync();

        Assert.Empty(ana.SkillDeltas);
    }

    [Fact]
    public async Task AnUnknownSkillAndANegativeGrantAreRefused()
    {
        await _test.JoinAsync("Ana");

        Assert.Throws<ArgumentException>(() => Skills.Grant("ana", "dancing", 1));
        Assert.Throws<ArgumentException>(() => Skills.XpOf("ana", "dancing"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Skills.Grant("ana", "mining", -1));
    }

    [Fact]
    public void APlayerWhoIsNotInTheWorldGainsNothing()
    {
        Assert.Empty(Skills.Grant("nobody", "mining", 100));
        Assert.Equal(0, Skills.XpOf("nobody", "mining"));
        Assert.Equal(1, Skills.LevelOf("nobody", "mining"));
    }

    [Fact]
    public async Task ExperienceStartsAgainForAPlayerWhoLeavesAndReturns()
    {
        var ana = await _test.JoinAsync("Ana");
        Skills.Grant("ana", "mining", 500);
        await _test.LeaveAsync(ana);
        await _test.TickAsync(30);

        await _test.JoinAsync("Ana");

        Assert.Equal(0, Skills.XpOf("ana", "mining"));
    }
}

/// <summary>What a client was told about its skills, read the way the skill tests ask.</summary>
file static class SkillReading
{
    extension(TestClient client)
    {
        /// <summary>The skill changes this player was sent, oldest first.</summary>
        public IReadOnlyList<Changes> SkillDeltas =>
        [
            .. client.Messages.OfType<PrivateState>()
                .Where(state => state.Delta.Span[0] == SkillSet.DeltaTag)
                .Select(state => Changes.TryDecode(DefaultSkills.All.Schema, state.Delta.Span[1..])!),
        ];
    }
}
