using Orblit.Dialogue;
using Orblit.Quest;
using Scape.Content;
using Scape.Core;

namespace Scape.Server.Tests;

public sealed class TalkTests : IAsyncDisposable
{
    private static readonly QuestDefinition Drill = new("drill", "Drill", [new Stage("a", "a"), new Stage("b", "b")]);

    private readonly PlacedNpc _brindle;
    private readonly PlacedNpc _guard;
    private readonly PlacedNpc _keeper;
    private TestServer _test = new();

    /// <summary>Puts the three speakers beside the spawn.</summary>
    public TalkTests()
    {
        _brindle = Server.Npcs.Spawn(new NpcSpawn(NpcId.Lamplighter, At(31, 32)));
        _guard = Server.Npcs.Spawn(new NpcSpawn(NpcId.Guard, At(33, 32)));
        _keeper = Server.Npcs.Spawn(new NpcSpawn(NpcId.Shopkeeper, At(32, 34)));
    }

    private ScapeServer Server => _test.Server;

    // The default story is on the test server.
    private Quests Quests => Server.Quests!;

    public ValueTask DisposeAsync() => _test.DisposeAsync();

    private static TileCoord At(int x, int y) => new(x, y, 0);

    private static Dictionary<string, long> Lantern(long stage) => new() { ["lantern"] = stage };

    private async Task<TestClient> JoinAsync(string name)
    {
        var client = await _test.JoinAsync(name);
        await _test.TickAsync();
        return client;
    }

    private async Task SayAsync(TestClient client, PlacedNpc npc)
    {
        await client.ClickAsync(InteractKind.Npc, (long)npc.NetworkId, NpcOption.Talk);
        await _test.TickAsync(6);
    }

    private async Task PressAsync(TestClient client, ClientMessage message)
    {
        await client.SendAsync(message);
        await _test.TickAsync();
    }

    /// <summary>Brindle's ask, the Guard's word and Brindle's thanks.</summary>
    private async Task LightLanternAsync(TestClient ana)
    {
        await SayAsync(ana, _brindle);
        await PressAsync(ana, new Continue());
        await PressAsync(ana, new Choose(0));
        await PressAsync(ana, new Continue());
        await SayAsync(ana, _guard);
        await PressAsync(ana, new Continue());
        await SayAsync(ana, _brindle);
        await PressAsync(ana, new Continue());
    }

    // A quest.

    [Fact]
    public async Task AQuestStartsAdvancesAndCompletesWithItsReward()
    {
        var ana = await JoinAsync("Ana");

        await SayAsync(ana, _brindle);
        await PressAsync(ana, new Continue());
        await PressAsync(ana, new Choose(0));
        Assert.Equal(Lantern(1), Quests.StagesOf("ana"));
        await PressAsync(ana, new Continue());
        await SayAsync(ana, _guard);
        await PressAsync(ana, new Continue());
        Assert.Equal(Lantern(2), Quests.StagesOf("ana"));
        await SayAsync(ana, _brindle);

        Assert.Equal(Lantern(3), Quests.StagesOf("ana"));
        Assert.Equal(80, Server.Skills.XpOf("ana", "firemaking"));
        Assert.Equal(25, Server.Items.ContainerOf("ana", ContainerId.Inventory)!.CountOf(ItemId.Coins));
        Assert.Contains("Quest complete: The Dark Lantern", ana.Notices);
    }

    [Fact]
    public async Task AQuestGivesNothingASecondTime()
    {
        var ana = await JoinAsync("Ana");
        await LightLanternAsync(ana);

        await SayAsync(ana, _brindle);
        await SayAsync(ana, _guard);

        Assert.Equal(80, Server.Skills.XpOf("ana", "firemaking"));
        Assert.Equal(["Quest complete: The Dark Lantern"], ana.Notices.Where(notice => notice.StartsWith("Quest complete", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AQuestPutsARewardThatDoesNotFitAtThePlayersFeet()
    {
        var ana = await JoinAsync("Ana");
        Server.Items.ContainerOf("ana", ContainerId.Inventory)!.Add(ItemId.Bones, Container.InventorySize);

        await LightLanternAsync(ana);

        var tile = Server.Movement.TileOf("ana")!.Value;
        var lying = Assert.Single(Server.Ground.LyingAt("ana", tile));
        Assert.Equal(ItemId.Coins, lying.Item);
        Assert.Equal(25, lying.Count);
        Assert.Empty(Server.Ground.LyingAt("bo", tile));
    }

    [Fact]
    public async Task AQuestDropsASavedStageTheGameNoLongerHas()
    {
        await JoinAsync("Ana");

        var dropped = Quests.Restore("ana", new Dictionary<string, long> { ["lantern"] = 2, ["gone"] = 1 });

        Assert.Equal(1, dropped);
        Assert.Equal(Lantern(2), Quests.StagesOf("ana"));
    }

    [Fact]
    public async Task AQuestDropsASavedStagePastTheLastOfItsQuest()
    {
        await JoinAsync("Ana");

        var dropped = Quests.Restore("ana", Lantern(4));

        Assert.Equal(1, dropped);
        Assert.Empty(Quests.StagesOf("ana"));
    }

    [Fact]
    public async Task AQuestKeepsASavedStageAtTheLastOfItsQuest()
    {
        await JoinAsync("Ana");

        var dropped = Quests.Restore("ana", Lantern(3));

        Assert.Equal(0, dropped);
        Assert.Equal(Lantern(3), Quests.StagesOf("ana"));
    }

    [Fact]
    public void AQuestLeavesOutEverySavedStageOfAPlayerWhoIsNotInTheWorld() =>
        Assert.Equal(2, Quests.Restore("ana", new Dictionary<string, long> { ["lantern"] = 1, ["tally"] = 1 }));

    [Fact]
    public async Task AQuestSendsNothingWhenARestoreChangesNothing()
    {
        var ana = await JoinAsync("Ana");
        var sent = ana.Messages.Count(message => message is PrivateState);

        Quests.Restore("ana", new Dictionary<string, long>());
        await _test.TickAsync();

        Assert.Equal(sent, ana.Messages.Count(message => message is PrivateState));
    }

    [Fact]
    public async Task AQuestTableGoesWhenThePlayerLeaves()
    {
        var ana = await JoinAsync("Ana");
        Quests.Restore("ana", Lantern(1));

        await ana.Link.CloseAsync();
        await _test.TickAsync(12);

        Assert.Empty(Quests.StagesOf("ana"));
    }

    [Fact]
    public async Task AQuestRefusesAWriteFromATableThatIsNoLongerCurrent()
    {
        await JoinAsync("Ana");
        var stale = Quests.TableOf("ana")!;
        Quests.Restore("ana", new Dictionary<string, long> { ["tally"] = 1 });
        var to = stale.Set(QuestNames.StageName("lantern"), 3).Table;

        var applied = Quests.Commit("ana", stale, to);

        Assert.False(applied);
        Assert.Equal(new Dictionary<string, long> { ["tally"] = 1 }, Quests.StagesOf("ana"));
        Assert.Equal(0, Server.Skills.XpOf("ana", "firemaking"));
    }

    [Fact]
    public async Task AQuestAnnouncesTheLevelsARewardReachesAndRefitsHealth()
    {
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Story = WithQuest(Drill, XpIn("health", 5000)) });
        var ana = await JoinAsync("Ana");
        var before = Server.Combat.HealthOfPlayer("ana")!.Value.Max;
        var table = Quests.TableOf("ana")!;

        Quests.Commit("ana", table, table.Set(QuestNames.StageName("drill"), 1).Table);
        await _test.TickAsync();

        var level = Server.Skills.LevelOf("ana", "health");
        Assert.True(level > 1);
        Assert.Contains($"You reached level {level} in health.", ana.Notices);
        Assert.True(Server.Combat.HealthOfPlayer("ana")!.Value.Max > before);
    }

    // A conversation.

    [Fact]
    public async Task AConversationChangesWithTheStage()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _guard);
        Assert.Equal("Move along.", Assert.IsType<ConversationLine>(ana.Shown).Text);
        await PressAsync(ana, new Continue());

        await SayAsync(ana, _brindle);
        await PressAsync(ana, new Continue());
        await PressAsync(ana, new Choose(0));
        await PressAsync(ana, new Continue());
        await SayAsync(ana, _guard);

        Assert.Equal("The tower is safe by day. Tell the lamplighter.", Assert.IsType<ConversationLine>(ana.Shown).Text);
    }

    [Fact]
    public async Task AConversationOffersAChoiceOnlyWhileItsConditionHolds()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _keeper);
        await PressAsync(ana, new Continue());
        Assert.Equal(["Just looking."], Assert.IsType<ConversationChoices>(ana.Shown).Options);
        await PressAsync(ana, new Choose(0));

        await LightLanternAsync(ana);
        await SayAsync(ana, _keeper);
        await PressAsync(ana, new Continue());

        Assert.Equal(["Can I help?", "Just looking."], Assert.IsType<ConversationChoices>(ana.Shown).Options);
    }

    [Fact]
    public async Task AConversationRefusesAChoiceThatDoesNotExist()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _brindle);
        await PressAsync(ana, new Continue());

        await PressAsync(ana, new Choose(2));
        await PressAsync(ana, new Continue());

        Assert.Equal(["That is not on offer.", "That is not on offer."], ana.Notices);
        Assert.Empty(Quests.StagesOf("ana"));
        Assert.IsType<ConversationChoices>(ana.Shown);
    }

    [Fact]
    public async Task AConversationRefusesAChoiceForALine()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _guard);

        await PressAsync(ana, new Choose(0));

        Assert.Equal(["That is not on offer."], ana.Notices);
    }

    [Fact]
    public async Task AConversationEndsWhenTheQuestsChangedUnderItWritingNothing()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _brindle);
        Quests.Restore("ana", new Dictionary<string, long> { ["tally"] = 1 });

        await PressAsync(ana, new Continue());

        Assert.IsType<WindowClosed>(ana.Shown);
        Assert.Equal(new Dictionary<string, long> { ["tally"] = 1 }, Quests.StagesOf("ana"));
    }

    [Fact]
    public async Task AConversationSaysThereIsNothingToSayToACreatureWithNoDialogue()
    {
        var ana = await JoinAsync("Ana");
        var chicken = Server.Npcs.Spawn(new NpcSpawn(NpcId.Chicken, At(33, 33)));

        await SayAsync(ana, chicken);

        Assert.Equal(["They have nothing to say."], ana.Notices);
        Assert.Null(ana.Shown);
    }

    [Fact]
    public async Task AConversationNoVariantStartsHasNothingToSay()
    {
        var dialogue = Parse(
            """
            "a":{"type":"line","speaker":"Ann","text":"Hello."}
            """,
            """{"start":"a","when":[{"name":"quest.lantern.stage","op":">=","value":3}]}""");
        await _test.DisposeAsync();
        _test = new TestServer(options => options with { Npcs = Speaker(), Story = StoryOf(dialogue) });
        var ann = Server.Npcs.Spawn(new NpcSpawn(1, At(31, 32)));
        var ana = await JoinAsync("Ana");

        await SayAsync(ana, ann);

        Assert.Equal(["They have nothing to say."], ana.Notices);
        Assert.Null(ana.Shown);
    }

    [Fact]
    public async Task AConversationClosesItsWindowAtTheEnd()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _guard);

        await PressAsync(ana, new Continue());

        Assert.IsType<WindowClosed>(ana.Shown);
    }

    [Fact]
    public async Task AConversationGoesNoFurtherOnceItsWindowCloses()
    {
        var ana = await JoinAsync("Ana");
        await SayAsync(ana, _brindle);
        await ana.SendAsync(new Walk(At(32, 28)));
        await _test.TickAsync(6);

        await PressAsync(ana, new Continue());

        Assert.IsType<WindowClosed>(ana.Shown);
    }

    // Starting.

    private static DialogueDefinition Parse(string nodes, string variant = """{"start":"a"}""") => DialogueDefinition.Parse(
        $$"""
        {"kind":"orblit.dialogue","formatVersion":1,"key":"d",
         "variants":[ {{variant}} ],"nodes":{ {{nodes}} } }
        """);

    private static DialogueDefinition ChoiceOf(int count)
    {
        var options = string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{"text":"Pick {{i}}"}"""));
        return Parse($$"""
            "a":{"type":"choice","options":[{{options}}]}
            """);
    }

    private static QuestReward XpIn(string skill, int xp = 1) => new(xp: new Dictionary<string, int> { [skill] = xp });

    /// <summary>The default story with <paramref name="quest"/> added at the end, giving <paramref name="reward"/>.</summary>
    private static Story WithQuest(QuestDefinition quest, QuestReward reward)
    {
        var story = DefaultStory.Story;
        return new Story(
            [.. story.Book.Quests, quest],
            story.Dialogues,
            story.Steps,
            new Dictionary<string, QuestReward>(story.Rewards) { [quest.Key] = reward });
    }

    private static NpcSet Speaker(string dialogue = "d") => new([new NpcDef(1, "Ann", dialogue: dialogue)]);

    private static Story StoryOf(DialogueDefinition dialogue) =>
        new([DefaultStory.Story.Book.Quest("lantern")], [dialogue]);

    private static void AssertRefused(NpcSet npcs, Story? story, string why) =>
        Assert.Contains(why, Assert.Throws<ArgumentException>(() => new TestServer(options => options with { Npcs = npcs, Story = story })).Message, StringComparison.Ordinal);

    [Fact]
    public void StartingRefusesACreatureWhoseDialogueIsNotInTheStory() => AssertRefused(Speaker("zz"), DefaultStory.Story, "which is not a dialogue");

    [Fact]
    public void StartingRefusesADialogueWhenThereIsNoStory() => AssertRefused(Speaker(), null, "needs a story");

    [Fact]
    public void StartingRefusesADialogueThatWritesAQuestStageItself() =>
        AssertRefused(Speaker(), StoryOf(Parse("""
            "a":{"type":"set","writes":[{"name":"quest.lantern.stage","value":3}]}
            """)), "only a step may do");

    [Fact]
    public void StartingRefusesALineTheClientCannotShow()
    {
        var text = new string('x', Wire.MaxTextBytes + 1);

        AssertRefused(Speaker(), StoryOf(Parse($$"""
            "a":{"type":"line","speaker":"Ann","text":"{{text}}"}
            """)), "text that is not valid");
    }

    [Fact]
    public void StartingRefusesAChoiceWithTooManyOptions() =>
        AssertRefused(Speaker(), StoryOf(ChoiceOf(Wire.MaxChoices + 1)), "too many choices");

    [Fact]
    public async Task StartingTakesAChoiceWithAsManyOptionsAsTheClientShows()
    {
        await using var test = new TestServer(options => options with { Npcs = Speaker(), Story = StoryOf(ChoiceOf(Wire.MaxChoices)) });

        Assert.NotNull(test.Server.Quests);
    }

    [Fact]
    public void StartingRefusesAQuestTitleANoticeCannotCarry()
    {
        var quest = new QuestDefinition("long", new string('x', Wire.MaxTextBytes), Drill.Stages);

        AssertRefused(DefaultNpcs.All, WithQuest(quest, XpIn("firemaking")), "is not valid text");
    }

    [Fact]
    public void StartingRefusesARewardInASkillTheGameDoesNotHave() =>
        AssertRefused(DefaultNpcs.All, WithQuest(Drill, XpIn("juggling")), "juggling is not a skill");

    [Fact]
    public void StartingRefusesARewardOfAnItemTheGameDoesNotHave() =>
        AssertRefused(DefaultNpcs.All, WithQuest(Drill, new(items: [new ItemStack(ItemDef.MaxId, 1)])), $"{ItemDef.MaxId} is not an item");
}

/// <summary>What a client was shown of conversations.</summary>
file static class TalkReading
{
    extension(TestClient client)
    {
        /// <summary>The conversation message that came last, or null when none has.</summary>
        public ServerMessage? Shown => client.Messages.LastOrDefault(message => message is ConversationLine or ConversationChoices or WindowClosed);
    }
}
