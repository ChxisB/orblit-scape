using System.Collections.Concurrent;
using System.Text;
using Orblit.Json;
using Orblit.Save;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class AdminTests : IAsyncDisposable
{
    private const string Password = "correct horse";

    /// <summary>A time with no part of a millisecond, so a save stamped with it reads back the same.</summary>
    private static readonly DateTimeOffset Midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, Role> Staff = new() { ["Ana"] = Role.Admin, ["Mo"] = Role.Moderator };

    private readonly List<TestServer> _servers = [];
    private readonly List<DirectoryInfo> _dirs = [];

    // The rollback logs from the pool while a test reads.
    private readonly ConcurrentQueue<string> _logged = [];
    private readonly FastHasher _hasher = new();
    private readonly MovedClock _clock = new();
    private readonly LimitedPacer _pacer = new();
    private ISaveStore _store = new MemorySaveStore();

    public async ValueTask DisposeAsync()
    {
        foreach (var test in _servers)
        {
            await test.DisposeAsync();
        }

        foreach (var dir in _dirs)
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void RolesNeedAccountsBecauseANameAloneProvesNothing()
    {
        Assert.Throws<ArgumentException>(() => Start(accounts: false));
    }

    [Fact]
    public async Task APlayerWhoHoldsNoRoleIsToldTheCommandDoesNotExist()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Bo"]);

        await SayAsync(test, who["Bo"], "/kick ana");

        Assert.Contains("Unknown command.", who["Bo"].Notices);
        Assert.Empty(who["Ana"].Goodbyes);
        Assert.Empty(Heard(who["Ana"]));
        Assert.Contains("Admin: bo was refused \"kick\"", _logged);
    }

    [Fact]
    public async Task AModeratorRunsAModeratorCommandAndNotAnAdminOne()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);

        await SayAsync(test, who["Mo"], "/give bo 2");
        await SayAsync(test, who["Mo"], "/mute bo 5");

        Assert.Equal(["Unknown command.", "bo is muted for 5 minutes."], who["Mo"].Notices);
    }

    [Fact]
    public async Task AModeratorCannotSanctionAnAdminOrAnotherModerator()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Mo"]);

        await SayAsync(test, who["Mo"], "/ban ana 0");
        await SayAsync(test, who["Mo"], "/kick mo");
        await SayAsync(test, who["Mo"], "/mute ana 5");

        Assert.Equal(["You cannot do that to ana.", "You cannot do that to mo.", "You cannot do that to ana."], who["Mo"].Notices);
        Assert.Empty(who["Ana"].Goodbyes);
    }

    [Fact]
    public async Task AnAdminMaySanctionAModeratorWhoCannotLiftIt()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Mo"]);

        await SayAsync(test, who["Ana"], "/mute mo 5");
        await SayAsync(test, who["Mo"], "/unmute mo");

        Assert.Equal(["mo is muted for 5 minutes."], who["Ana"].Notices);
        Assert.Equal("You cannot lift a sanction on yourself.", who["Mo"].Notices[^1]);
    }

    [Fact]
    public async Task RolesAreSetByNameWhateverTheCase()
    {
        var test = Start(new Dictionary<string, Role> { ["ANA"] = Role.Admin });
        var who = await CrowdAsync(test, ["Ana"]);

        await SayAsync(test, who["Ana"], "/cancelshutdown");

        Assert.Equal(["No shutdown is set."], who["Ana"].Notices);
    }

    [Fact]
    public async Task BadInputIsToldHowToWriteItAndNeverThrows()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Mo", "Bo"]);
        test.Map.SetFlags(new TileCoord(40, 42, 0), TileFlags.Water);
        (string Name, string Line, string Expected)[] cases =
        [
            ("Mo", "/", "Unknown command."),
            ("Mo", "/KICK nobody", "nobody is not online."),
            ("Mo", "/kick\ufeffnobody", "nobody is not online."),
            ("Mo", "/kick\u0085nobody", "Unknown command."),
            ("Mo", "/mute", "Use /mute"),
            ("Mo", "/mute bo", "Use /mute"),
            ("Mo", "/mute bo soon", "Use /mute"),
            ("Mo", "/mute bo -5", "Use /mute"),
            ("Mo", "/mute bo 99999999999999", "Use /mute"),
            ("Mo", "/mute bo 0x10", "Use /mute"),
            ("Mo", "/mute bo +5", "Use /mute"),
            ("Mo", "/mute bo -0", "Use /mute"),
            ("Mo", "/mute bo 1073741825", "Use /mute"),
            ("Mo", "/ban \"Ana 5", "is not a valid name."),
            ("Mo", "/kick nobody", "nobody is not online."),
            ("Mo", "/unmute bo", "bo is not muted."),
            ("Mo", "/unban bo", "bo is not banned."),
            ("Ana", "/give bo 9999", "There is no such item."),
            ("Ana", "/give bo 0", "Use /give"),
            ("Ana", FormattableString.Invariant($"/give bo {ItemId.Logs} 0"), "Use /give"),
            ("Ana", "/skill bo nothing 5", "There is no such skill."),
            ("Ana", "/teleport bo 99999 99999", "That tile is outside the world."),
            ("Ana", "/teleport bo 40 42", "Nobody can stand there."),
            ("Ana", "/shutdown 0", "Use /shutdown"),
            ("Ana", "/cancelshutdown", "No shutdown is set."),
            ("Ana", "/rollback bo 0", "Use /rollback"),
            ("Ana", "/rollback bo 5", "This server keeps no save history."),
            ("Ana", "/broadcast", "Use /broadcast"),
        ];

        foreach (var (name, line, expected) in cases)
        {
            var told = who[name].Notices.Count;
            await SayAsync(test, who[name], line);

            // One new notice each, so a line that said nothing cannot pass on the last one's answer.
            Assert.Equal(told + 1, who[name].Notices.Count);
            Assert.Contains(expected, who[name].Notices[^1], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AQuotedNameMayHoldSpaces()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo"]);

        await SayAsync(test, who["Mo"], "/mute \"Bo Bo\" 5");

        Assert.Equal(["bo bo is muted for 5 minutes."], who["Mo"].Notices);
    }

    [Fact]
    public async Task EachCommandDoesItsWorkAndWritesALogLine()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Mo", "Bo", "Di"]);
        (string Name, string Line, string Entry)[] lines =
        [
            ("Mo", "/mute bo 5 loud", "Admin: mo mute bo: loud, for 5 minutes"),
            ("Mo", "/unmute bo", "Admin: mo unmute bo: lifted"),
            ("Mo", "/ban cy 0", "Admin: mo ban cy: no reason given, with no end"),
            ("Mo", "/unban cy", "Admin: mo unban cy: lifted"),
            ("Ana", "/teleport bo 40 41", "Admin: ana teleport bo: to 40 41 0"),
            ("Ana", FormattableString.Invariant($"/give bo {ItemId.Logs} 3"), "Admin: ana give bo: 3 of 3 x Logs"),
            ("Ana", "/skill bo fishing 250", "Admin: ana skill bo: fishing to 250"),
            ("Ana", "/broadcast hello all", "Admin: ana broadcast everyone: hello all"),
            ("Ana", "/shutdown 30", "Admin: ana shutdown everyone: in 30 seconds"),
            ("Ana", "/cancelshutdown", "Admin: ana cancelshutdown everyone: cancelled"),
            ("Mo", "/kick di spam", "Admin: mo kick di: spam"),
        ];

        foreach (var (name, line, entry) in lines)
        {
            await SayAsync(test, who[name], line);
            Assert.Contains(entry, _logged);
        }

        await KickedAsync(test, who["Di"]);
        Assert.Equal(new TileCoord(40, 41, 0), test.Server.Movement.Standing("bo")?.Tile);
        Assert.Equal(3L, test.Server.Items.ContainerOf("bo", ContainerId.Inventory)?.CountOf(ItemId.Logs));
        Assert.Equal(250L, test.Server.Skills.XpOf("bo", "fishing"));
        Assert.Contains("[Server] hello all", who["Bo"].Notices);
        Assert.Contains("You were kicked: spam", who["Di"].Notices);
    }

    [Fact]
    public async Task AKickIsNotABan()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);
        await SayAsync(test, who["Mo"], "/kick bo");
        await KickedAsync(test, who["Bo"]);

        var again = await EnterAsync(test, "Bo");

        Assert.True(again.Admitted);
    }

    [Fact]
    public async Task APlayerWhoHasLeftIsNotOnline()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);
        await test.LeaveAsync(who["Bo"]);
        await test.TickAsync(12);

        await SayAsync(test, who["Mo"], "/kick bo");

        Assert.Equal(["bo is not online."], who["Mo"].Notices);
    }

    [Fact]
    public async Task AKickReasonFillsTheNoticeToItsLastByte()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Di"]);
        var told = "You were kicked: " + new string('a', Wire.MaxTextBytes - "You were kicked: ".Length);

        await SayAsync(test, who["Mo"], "/kick di " + told["You were kicked: ".Length..]);
        await KickedAsync(test, who["Di"]);

        Assert.Contains(told, who["Di"].Notices);
    }

    [Fact]
    public async Task ALineThatIsACommandIsNeverSaidAloud()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Bo", "Cy"]);

        await SayAsync(test, who["Bo"], "/hello");
        await SayAsync(test, who["Bo"], "hello");

        Assert.Equal(["hello"], Heard(who["Cy"]));
    }

    [Fact]
    public async Task MutesHoldAcrossARestart()
    {
        var first = Start();
        var before = await CrowdAsync(first, ["Mo", "Bo"]);
        await SayAsync(first, before["Bo"], "before");
        await SayAsync(first, before["Mo"], "/mute bo 0 rude");
        await SayAsync(first, before["Bo"], "muted");
        await first.DisposeAsync();

        var second = Start();
        var after = await CrowdAsync(second, ["Mo", "Bo"], create: false);
        await SayAsync(second, after["Bo"], "after");

        Assert.Equal(["before"], Heard(before["Mo"]));
        Assert.Empty(Heard(after["Mo"]));
        Assert.Contains("You are muted: rude", before["Bo"].Notices);
        Assert.Contains("You are muted.", after["Bo"].Notices);
    }

    [Fact]
    public async Task MutesEndByThemselves()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);
        await SayAsync(test, who["Mo"], "/mute bo 5");
        _clock.Offset = TimeSpan.FromSeconds(299);
        await test.TickAsync();
        await SayAsync(test, who["Bo"], "quiet");
        Assert.Empty(Heard(who["Mo"]));

        _clock.Offset = TimeSpan.FromSeconds(301);
        await test.TickAsync();
        await SayAsync(test, who["Bo"], "loud");

        Assert.Contains("You can chat again.", who["Bo"].Notices);
        Assert.Equal(["loud"], Heard(who["Mo"]));
    }

    [Fact]
    public async Task MutesApplyToALineSentInTheSameTick()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);

        await who["Mo"].SendAsync(new Chat("/mute bo 0"));
        await who["Bo"].SendAsync(new Chat("too late"));
        await test.TickAsync(2);

        Assert.Empty(Heard(who["Mo"]));
    }

    [Fact]
    public async Task MutesAreLiftedByUnmute()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);
        await SayAsync(test, who["Mo"], "/mute bo 0");

        await SayAsync(test, who["Mo"], "/unmute bo");
        await SayAsync(test, who["Bo"], "free");

        Assert.Equal(["free"], Heard(who["Mo"]));
    }

    [Fact]
    public async Task BansKeepAPlayerOutAfterARestartUntilTheyAreLifted()
    {
        var first = Start();
        var before = await CrowdAsync(first, ["Mo", "Bo"]);
        await SayAsync(first, before["Mo"], "/ban bo 0 cheating");
        await KickedAsync(first, before["Bo"]);
        await first.DisposeAsync();

        var second = Start();
        var mo = await EnterAsync(second, "Mo");
        var refused = await EnterAsync(second, "Bo");
        await SayAsync(second, mo, "/unban bo");
        var allowed = await EnterAsync(second, "Bo");

        Assert.Equal([LoginRefusal.Banned.ToWire()], refused.Goodbyes);
        Assert.True(allowed.Admitted);
    }

    [Fact]
    public async Task ABanThatLandsWhileALoginReadsTheSaveStillStopsIt()
    {
        var hooked = new HookedStore(new MemorySaveStore());
        _store = hooked;
        var test = Start();
        var mo = await EnterAsync(test, "Mo", create: true);
        hooked.OnRead = async key =>
        {
            if (key != StoreKeys.SaveOf("bo"))
            {
                return;
            }

            hooked.OnRead = null;
            await mo.SendAsync(new Chat("/ban bo 0"));
            test.Server.Loop.RunTick();
        };

        var bo = await EnterAsync(test, "Bo", create: true);

        Assert.Equal([LoginRefusal.Banned.ToWire()], bo.Goodbyes);
    }

    [Fact]
    public async Task BansWithALengthEndByThemselves()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Mo", "Bo"]);
        await SayAsync(test, who["Mo"], "/ban bo 10");
        await KickedAsync(test, who["Bo"]);
        _clock.Offset = TimeSpan.FromMinutes(11);

        var again = await EnterAsync(test, "Bo");

        Assert.True(again.Admitted);
    }

    [Fact]
    public async Task BansAreCheckedAfterThePassword()
    {
        var first = Start();
        var before = await CrowdAsync(first, ["Mo", "Bo"]);
        await SayAsync(first, before["Mo"], "/ban bo 0");
        await first.DisposeAsync();
        var second = Start();

        var guess = await second.JoinAsync("Bo", "wrong horse");

        Assert.Equal([LoginRefusal.BadLogin.ToWire()], guess.Goodbyes);
    }

    [Fact]
    public async Task ADamagedLedgerKeepsEveryoneOutAndIsNotWrittenOver()
    {
        var bytes = Encoding.UTF8.GetBytes("not json");
        await _store.WriteAsync(StoreKeys.Ledger, LedgerRecord(bytes));
        var test = Start();

        var who = await EnterAsync(test, "Bo", create: true);

        Assert.Equal([LoginRefusal.Unavailable.ToWire()], who.Goodbyes);
        var kept = await _store.ReadAsync(StoreKeys.Ledger);
        Assert.NotNull(kept);
        Assert.Equal(bytes, kept.Bytes.ToArray());
    }

    [Fact]
    public async Task ShutdownCountsDownTellsEveryoneThenStopsTheLoop()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Bo"]);
        await SayAsync(test, who["Ana"], "/shutdown 3");

        _pacer.Allowed = 20;
        await test.Server.Loop.RunAsync();
        await test.PumpAsync();

        Assert.True(_pacer.Allowed > 10);
        Assert.Equal(
            [
                "[Server] The server shuts down in 3 seconds.",
                "[Server] The server shuts down in 2 seconds.",
                "[Server] The server shuts down in 1 second.",
            ],
            who["Bo"].Notices);
    }

    [Fact]
    public async Task ShutdownRoundsAPartTickUp()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana", "Bo"]);
        await SayAsync(test, who["Ana"], "/shutdown 1");

        _pacer.Allowed = 20;
        await test.Server.Loop.RunAsync();
        await test.PumpAsync();

        // Two ticks, 1.2 s, so the count says 2 seconds after 1 second. Decision 80 item 105.
        Assert.Equal(19, _pacer.Allowed);
        Assert.Equal(
            [
                "[Server] The server shuts down in 1 second.",
                "[Server] The server shuts down in 2 seconds.",
                "[Server] The server shuts down in 1 second.",
            ],
            who["Bo"].Notices);
    }

    [Fact]
    public async Task ShutdownOfTheLongestTimeDoesNotStopAtOnce()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana"]);
        await SayAsync(test, who["Ana"], "/shutdown 1073741824");

        _pacer.Allowed = 20;

        await Assert.ThrowsAsync<InvalidOperationException>(test.Server.Loop.RunAsync);
    }

    [Fact]
    public async Task ShutdownDoesNotStopOnceItIsCancelled()
    {
        var test = Start();
        var who = await CrowdAsync(test, ["Ana"]);
        await SayAsync(test, who["Ana"], "/shutdown 3");
        await SayAsync(test, who["Ana"], "/cancelshutdown");

        _pacer.Allowed = 20;

        await Assert.ThrowsAsync<InvalidOperationException>(test.Server.Loop.RunAsync);
    }

    [Fact]
    public async Task RollbackLogsThePlayerOutFirstThenPutsTheirSaveBack()
    {
        var history = TempHistory();
        _store = history;
        var test = Start(history: history);
        var bo = await EnterAsync(test, "Bo", create: true);
        test.Server.Skills.Grant("bo", "fishing", 100);
        await test.LeaveAsync(bo);
        await test.TickAsync(12);
        _clock.Offset = TimeSpan.FromMinutes(30);
        var back = await EnterAsync(test, "Bo");
        test.Server.Skills.Grant("bo", "fishing", 900);
        var ana = await EnterAsync(test, "Ana", create: true);

        await ana.SendAsync(new Chat("/rollback bo 20"));
        await test.TickAsync();
        await test.UntilAsync(() => _logged.Any(line => line.StartsWith("Admin: ana rollback", StringComparison.Ordinal)));
        await test.TickAsync();
        var again = await EnterAsync(test, "Bo");

        Assert.StartsWith("You are logged out", back.Notices[0], StringComparison.Ordinal);
        Assert.NotEmpty(back.Goodbyes);
        Assert.StartsWith("bo was rolled back to 2026-01-01", ana.Notices[^1], StringComparison.Ordinal);
        Assert.Equal(100L, test.Server.Skills.XpOf("bo", "fishing"));
        Assert.True(again.Admitted);
    }

    [Fact]
    public async Task RollbackLocksThePlayerWhileItRunsAndSaysWhenNothingIsOldEnough()
    {
        var history = TempHistory();
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var lockedAtLogOut = new List<bool>();
        var rollback = RollbackOn(history, ledger, player =>
        {
            lockedAtLogOut.Add(ledger.IsLocked(player));
            return Task.CompletedTask;
        });

        var said = await rollback.RunAsync("bo", 5);

        Assert.Equal([true], lockedAtLogOut);
        Assert.False(ledger.IsLocked("bo"));
        Assert.Equal("bo has no save from 5 minutes ago.", said);
    }

    [Fact]
    public async Task RollbackRefusesASecondRollbackOfANameThatIsLocked()
    {
        var history = TempHistory();
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var release = Stores.Gate();
        var rollback = RollbackOn(history, ledger, _ => release.Task);
        var first = rollback.RunAsync("bo", 5);

        var second = rollback.RunAsync("bo", 20);
        Assert.True(second.IsCompleted); // A refusal never waits on the first rollback.
        release.SetResult();
        await first;

        Assert.Equal("A rollback of bo is already running.", await second);
        Assert.False(ledger.IsLocked("bo"));
    }

    [Fact]
    public async Task RollbackAnswersAFailureAndStillUnlocks()
    {
        var history = TempHistory();
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var rollback = RollbackOn(history, ledger, _ => Task.FromException(new InvalidOperationException("stuck")));

        var said = await rollback.RunAsync("bo", 5);

        Assert.StartsWith("The rollback of bo failed: ", said, StringComparison.Ordinal);
        Assert.False(ledger.IsLocked("bo"));
    }

    [Fact]
    public async Task RollbackWaitsForTheLeaveSaveToReachTheStore()
    {
        var history = TempHistory();
        var failing = new SpyStore(history);
        failing.Failing.Add(StoreKeys.SaveOf("bo"));
        var queue = InlineQueue(failing);
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var options = OptionsOn(history, ledger, player =>
        {
            queue.Save(StoreKeys.SaveOf(player), "left"u8, new Custom("test"));
            return Task.CompletedTask;
        });

        var said = await new Rollback(options with { Queue = queue }).RunAsync("bo", 5);

        Assert.Equal("The save of bo is not on the store yet. Try again.", said);
    }

    [Fact]
    public async Task RollbackTakesASaveMadeExactlyThatManyMinutesAgo()
    {
        var history = TempHistory();
        await history.WriteAsync(StoreKeys.SaveOf("bo"), new SaveRecord("old"u8.ToArray(), new Custom("test"), Midnight));
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var options = OptionsOn(history, ledger, _ => Task.CompletedTask) with { Now = () => Midnight.AddMinutes(5) };

        var said = await new Rollback(options).RunAsync("bo", 5);

        Assert.Equal("bo was rolled back to 2026-01-01T00:00:00.000Z.", said);
    }

    [Fact]
    public async Task RollbackRefusesASaveThatIsStillInUse()
    {
        var history = TempHistory();
        await history.WriteAsync(StoreKeys.SaveOf("bo"), new SaveRecord("old"u8.ToArray(), new Custom("test"), Midnight));
        var ledger = new Ledger(InlineQueue(history), _clock.Read);
        var options = OptionsOn(history, ledger, _ => Task.CompletedTask) with
        {
            IsOnline = _ => true,
            Now = () => Midnight.AddMinutes(5),
        };

        var said = await new Rollback(options).RunAsync("bo", 5);

        Assert.Equal($"The rollback of bo failed: The save \"{StoreKeys.SaveOf("bo")}\" is in use", said);
    }

    [Fact]
    public async Task EntranceRefusesABannedPlayerBeforeItReadsTheirSave()
    {
        var ledger = new Ledger(InlineQueue(_store), _clock.Read);
        await ledger.LoadAsync();
        ledger.Add(SanctionKind.Ban, "bo", new SanctionOrder("ana", "x", null));
        var read = new List<string>();
        var entrance = new Entrance(player =>
        {
            read.Add(player);
            return Task.CompletedTask;
        }, null, ledger);

        var refused = await entrance.CheckAsync("bo", new Login("Bo"));

        Assert.Equal(LoginRefusal.Banned, refused);
        Assert.Empty(read);
    }

    [Fact]
    public async Task EntranceRefusesAPlayerWhoIsLockedOut()
    {
        var ledger = new Ledger(InlineQueue(_store), _clock.Read);
        ledger.Lock("bo");
        var entrance = new Entrance(_ => Task.CompletedTask, null, ledger);

        Assert.Equal(LoginRefusal.Unavailable, await entrance.CheckAsync("bo", new Login("Bo")));
        Assert.Null(await entrance.CheckAsync("cy", new Login("Cy")));
    }

    [Fact]
    public async Task LedgerKeepsSanctionsAcrossAReloadWithWhoAndWhy()
    {
        var queue = InlineQueue(_store);
        var first = new Ledger(queue, _clock.Read);
        await first.LoadAsync();
        first.Add(SanctionKind.Mute, "bo", new SanctionOrder("mo", "rude", TimeSpan.FromMinutes(5)));
        first.Add(SanctionKind.Ban, "cy", new SanctionOrder("ana", "cheat", null));
        await queue.FlushAsync();

        var second = new Ledger(queue, _clock.Read);
        await second.LoadAsync();

        var mute = second.Active(SanctionKind.Mute, "bo");
        Assert.NotNull(mute);
        Assert.Equal(("mo", "rude"), (mute.By, mute.Reason));
        Assert.Null(Assert.IsType<Sanction>(second.Active(SanctionKind.Ban, "cy")).Until);
        Assert.Null(second.Active(SanctionKind.Ban, "bo"));
        _clock.Offset = TimeSpan.FromMinutes(6);
        Assert.Null(second.Active(SanctionKind.Mute, "bo"));
    }

    [Fact]
    public async Task LedgerDropsASanctionThatHasEndedWhenItSaves()
    {
        var queue = InlineQueue(_store);
        var ledger = new Ledger(queue, _clock.Read);
        await ledger.LoadAsync();
        ledger.Add(SanctionKind.Mute, "bo", new SanctionOrder("mo", "x", TimeSpan.FromMinutes(5)));
        _clock.Offset = TimeSpan.FromMinutes(6);

        ledger.Add(SanctionKind.Ban, "cy", new SanctionOrder("ana", "cheat", null));
        await queue.FlushAsync();

        var record = await _store.ReadAsync(StoreKeys.Ledger);
        Assert.NotNull(record);
        var saved = Assert.IsType<OrderedDictionary<string, object?>>(JsonText.Decode(Encoding.UTF8.GetString(record.Bytes.Span)));
        Assert.Empty(Assert.IsType<OrderedDictionary<string, object?>>(saved["mute"]));
        Assert.Equal(["cy"], Assert.IsType<OrderedDictionary<string, object?>>(saved["ban"]).Keys);
    }

    [Fact]
    public async Task LedgerLiftRemovesASanctionForGood()
    {
        var queue = InlineQueue(_store);
        var first = new Ledger(queue, _clock.Read);
        await first.LoadAsync();
        first.Add(SanctionKind.Ban, "cy", new SanctionOrder("ana", "cheat", null));
        first.Lift(SanctionKind.Ban, "cy");
        await queue.FlushAsync();

        var second = new Ledger(queue, _clock.Read);
        await second.LoadAsync();

        Assert.Null(second.Active(SanctionKind.Ban, "cy"));
    }

    [Fact]
    public void LedgerRefusesAChangeBeforeItHasLoaded()
    {
        var ledger = new Ledger(InlineQueue(_store), _clock.Read);

        Assert.Throws<InvalidOperationException>(() => ledger.Add(SanctionKind.Ban, "cy", new SanctionOrder("ana", "x", null)));
        Assert.Throws<InvalidOperationException>(() => ledger.Lift(SanctionKind.Ban, "cy"));
    }

    [Fact]
    public async Task ADamagedLedgerRecordFailsEveryLoadUntilItIsFixed()
    {
        await _store.WriteAsync(StoreKeys.Ledger, LedgerRecord("{\"v\":2}"u8));
        var ledger = new Ledger(InlineQueue(_store), _clock.Read);

        await Assert.ThrowsAsync<FormatException>(ledger.LoadAsync);
        await Assert.ThrowsAsync<FormatException>(ledger.LoadAsync);
        await _store.WriteAsync(StoreKeys.Ledger, LedgerRecord("{\"v\":1,\"mute\":{},\"ban\":{\"cy\":{\"by\":\"ana\",\"reason\":\"x\",\"until\":null}}}"u8));
        await ledger.LoadAsync();

        Assert.NotNull(ledger.Active(SanctionKind.Ban, "cy"));
    }

    [Fact]
    public async Task ALedgerRecordThatIsNotTextIsDamaged()
    {
        await _store.WriteAsync(StoreKeys.Ledger, LedgerRecord([0xFF, 0xFE]));
        var ledger = new Ledger(InlineQueue(_store), _clock.Read);

        await Assert.ThrowsAsync<FormatException>(ledger.LoadAsync);
    }

    private static Task<TestClient> EnterAsync(TestServer test, string name, bool create = false) =>
        test.JoinAsync(name, Password, create);

    /// <summary>Logs each of <paramref name="names"/> in, all online at once.</summary>
    private static async Task<Dictionary<string, TestClient>> CrowdAsync(TestServer test, string[] names, bool create = true)
    {
        var who = new Dictionary<string, TestClient>();
        foreach (var name in names)
        {
            who[name] = await EnterAsync(test, name, create);
        }

        return who;
    }

    /// <summary>Sends <paramref name="text"/> and ticks twice: once to run it and once to deliver what the command told the player.</summary>
    private static async Task SayAsync(TestServer test, TestClient who, string text)
    {
        await who.SendAsync(new Chat(text));
        await test.TickAsync(2);
    }

    /// <summary>Waits for the goodbye, since a kick removes the player off the tick.</summary>
    private static Task KickedAsync(TestServer test, TestClient who) => test.UntilAsync(() => who.Goodbyes.Count > 0);

    private static string[] Heard(TestClient client) => [.. client.Messages.OfType<Said>().Select(said => said.Text)];

    /// <summary>
    /// A server on the test's store with accounts for everyone, Ana as an admin and Mo as a
    /// moderator unless <paramref name="roles"/> says otherwise. A restart is a second one on the
    /// same store.
    /// </summary>
    private TestServer Start(IReadOnlyDictionary<string, Role>? roles = null, bool accounts = true, HistoryStore? history = null)
    {
        var test = new TestServer(options => options with
        {
            Pacer = _pacer,
            Log = _logged.Enqueue,
            Roles = roles ?? Staff,
            Storage = new Storage
            {
                Queue = InlineQueue(_store),
                Hasher = accounts ? _hasher : null,
                History = history,
                Now = _clock.Read,
            },
        });
        _servers.Add(test);
        return test;
    }

    private SaveQueue InlineQueue(ISaveStore store) => SaveQueue.Inline(store, _clock.Read, (_, _) => { });

    private Rollback RollbackOn(HistoryStore history, Ledger ledger, Func<string, Task> logOut) => new(OptionsOn(history, ledger, logOut));

    /// <summary>A rollback on <paramref name="history"/> for a player who is offline, on the test's clock.</summary>
    private RollbackOptions OptionsOn(HistoryStore history, Ledger ledger, Func<string, Task> logOut) => new()
    {
        History = history,
        Queue = InlineQueue(history),
        Ledger = ledger,
        LogOut = logOut,
        IsOnline = _ => false,
        Now = _clock.Read,
    };

    private SaveRecord LedgerRecord(ReadOnlySpan<byte> bytes) => new(bytes.ToArray(), new Custom("test"), _clock.Read());

    /// <summary>A store that keeps a history, in a folder removed when the test ends.</summary>
    private HistoryStore TempHistory()
    {
        var dir = Directory.CreateTempSubdirectory("scape_admin");
        _dirs.Add(dir);
        return HistoryStore.Files(dir.FullName);
    }

    /// <summary>A clock the test moves by hand. Each read is a millisecond later, so two saves never share an instant.</summary>
    private sealed class MovedClock
    {
        private long _reads = -1;

        public TimeSpan Offset { get; set; }

        public DateTimeOffset Read() =>
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + Offset + TimeSpan.FromMilliseconds(Interlocked.Increment(ref _reads));
    }

    /// <summary>Allows a few ticks and then throws, so a loop that should have stopped fails the test and does not hang it.</summary>
    private sealed class LimitedPacer : IPacer
    {
        public int Allowed { get; set; }

        public Task UntilNextAsync() => Allowed-- <= 0
            ? Task.FromException(new InvalidOperationException("The loop did not stop."))
            : Task.CompletedTask;
    }

    /// <summary>A store that lets the test act in the middle of a read, after the save is found.</summary>
    private sealed class HookedStore(ISaveStore inner) : ISaveStore
    {
        public Func<string, Task>? OnRead { get; set; }

        public async Task<SaveRecord?> ReadAsync(string key)
        {
            var found = await inner.ReadAsync(key);
            if (OnRead is { } hook)
            {
                await hook(key);
            }

            return found;
        }

        public Task WriteAsync(string key, SaveRecord record) => inner.WriteAsync(key, record);

        public Task<bool> ExistsAsync(string key) => inner.ExistsAsync(key);

        public Task<IReadOnlyList<string>> KeysAsync() => inner.KeysAsync();
    }
}
