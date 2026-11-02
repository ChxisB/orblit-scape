using System.Text;
using Orblit.Json;
using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class PlayerStateTests
{
    private static readonly TileCoord Tile = new(1, 1, 0);

    private static ItemStack?[] SlotsOf(ContainerId id, Dictionary<int, ItemStack> held) =>
        [.. Enumerable.Range(0, Container.SizeOf(id)).Select(slot => held.TryGetValue(slot, out var stack) ? stack : (ItemStack?)null)];

    private static PlayerState Sample() => new(
        new TileCoord(40, 41, 1),
        skills: new Dictionary<string, long> { ["fighting"] = 1200, ["cooking"] = 0 },
        quests: new Dictionary<string, long> { ["lamp"] = 2, ["crabs"] = 1 },
        containers: new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>
        {
            [ContainerId.Inventory] = SlotsOf(ContainerId.Inventory, new() { [0] = new(Coins, 99), [27] = new(Logs, 1) }),
            [ContainerId.Bank] = SlotsOf(ContainerId.Bank, new() { [799] = new(Sword, 1) }),
        },
        friends: ["bo", "cy"],
        ignored: ["dee"]);

    private static byte[] Json(object? value) => Encoding.UTF8.GetBytes(JsonText.Encode(value));

    private static OrderedDictionary<string, object?> Good() =>
        (OrderedDictionary<string, object?>)JsonText.Decode(Encoding.UTF8.GetString(Sample().Encode()))!;

    private static byte[] With(string key, object? value)
    {
        var state = Good();
        state[key] = value;
        return Json(state);
    }

    private static byte[] Without(string key)
    {
        var state = Good();
        state.Remove(key);
        return Json(state);
    }

    private static void Refuses(byte[] bytes) => Assert.ThrowsAny<FormatException>(() => PlayerState.Decode(bytes));

    // A state.

    [Fact]
    public void AStateRoundTripsEveryPart()
    {
        var back = PlayerState.Decode(Sample().Encode());

        Assert.Equal(new TileCoord(40, 41, 1), back.Tile);
        Assert.Equal(new Dictionary<string, long> { ["fighting"] = 1200, ["cooking"] = 0 }, back.Skills);
        Assert.Equal(new Dictionary<string, long> { ["lamp"] = 2, ["crabs"] = 1 }, back.Quests);
        Assert.Equal(["bo", "cy"], back.Friends);
        Assert.Equal(["dee"], back.Ignored);
        Assert.Equal(Sample().Containers[ContainerId.Inventory], back.Containers[ContainerId.Inventory]);
        Assert.Equal(new ItemStack(Sword, 1), back.Containers[ContainerId.Bank][799]);
        Assert.False(back.Containers.ContainsKey(ContainerId.Equipment));
    }

    [Fact]
    public void AStateWithNothingButATileRoundTrips()
    {
        var back = PlayerState.Decode(new PlayerState(new TileCoord(1, 2, 0)).Encode());

        Assert.Equal(new TileCoord(1, 2, 0), back.Tile);
        Assert.Empty(back.Skills);
        Assert.Empty(back.Containers);
        Assert.Empty(back.Friends);
    }

    [Fact]
    public void AStateEncodesTheSameBytesWhateverOrderItWasBuiltIn()
    {
        var other = new PlayerState(
            new TileCoord(40, 41, 1),
            skills: new Dictionary<string, long> { ["cooking"] = 0, ["fighting"] = 1200 },
            quests: new Dictionary<string, long> { ["crabs"] = 1, ["lamp"] = 2 },
            containers: new Dictionary<ContainerId, IReadOnlyList<ItemStack?>>
            {
                [ContainerId.Bank] = Sample().Containers[ContainerId.Bank],
                [ContainerId.Inventory] = Sample().Containers[ContainerId.Inventory],
            },
            friends: ["bo", "cy"],
            ignored: ["dee"]);

        Assert.Equal(Sample().Encode(), other.Encode());
    }

    [Fact]
    public void AStateWritesOnlyTheSlotsThatHoldSomething()
    {
        var inventory = ((OrderedDictionary<string, object?>)Good()["containers"]!)["inventory"];

        Assert.Equal(JsonText.Encode(new object?[] { new object?[] { 0, Coins, 99 }, new object?[] { 27, Logs, 1 } }), JsonText.Encode(inventory));
    }

    [Fact]
    public void AStateCannotBeChangedThroughWhatItHandsOut()
    {
        var state = Sample();

        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)state.Skills)["x"] = 1);
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)state.Friends).Add("x"));
        Assert.Throws<NotSupportedException>(() => ((IList<ItemStack?>)state.Containers[ContainerId.Inventory])[5] = new ItemStack(Logs, 1));
    }

    [Fact]
    public void AStateKeepsWhatItWasBuiltFromEvenIfThatChangesAfterwards()
    {
        var skills = new Dictionary<string, long> { ["fighting"] = 5 };
        var state = new PlayerState(Tile, skills: skills);
        skills["fighting"] = 6;

        Assert.Equal(5, state.Skills["fighting"]);
    }

    // Building a state.

    [Fact]
    public void BuildingRefusesExperienceOutsideZeroToThe32BitLimit()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, skills: new Dictionary<string, long> { ["a"] = -1 }));
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, skills: new Dictionary<string, long> { ["a"] = PlayerState.MaxSavedXp + 1 }));
        Assert.Equal(PlayerState.MaxSavedXp, new PlayerState(Tile, skills: new Dictionary<string, long> { ["a"] = PlayerState.MaxSavedXp }).Skills["a"]);
    }

    [Fact]
    public void BuildingRefusesAQuestStageOutsideZeroToThe32BitLimit()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, quests: new Dictionary<string, long> { ["a"] = -1 }));
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, quests: new Dictionary<string, long> { ["a"] = PlayerState.MaxSavedXp + 1 }));
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, quests: new Dictionary<string, long> { [string.Empty] = 1 }));
    }

    [Fact]
    public void BuildingRefusesASkillWithNoId()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, skills: new Dictionary<string, long> { [string.Empty] = 1 }));
    }

    [Fact]
    public void BuildingRefusesAContainerOfTheWrongSize()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(
            Tile,
            containers: new Dictionary<ContainerId, IReadOnlyList<ItemStack?>> { [ContainerId.Inventory] = new ItemStack?[27] }));
    }

    // A cast can make a container id outside the enum.
    [Fact]
    public void BuildingRefusesAContainerOutsideTheEnum()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(
            Tile,
            containers: new Dictionary<ContainerId, IReadOnlyList<ItemStack?>> { [(ContainerId)9] = new ItemStack?[28] }));
    }

    [Fact]
    public void BuildingRefusesANameThatIsNotAPlayerKey()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, friends: ["Bo"]));
        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, ignored: ["no way too long"]));
    }

    [Fact]
    public void BuildingRefusesAListOverTheLimit()
    {
        var many = Enumerable.Range(0, Wire.MaxListEntries + 1).Select(i => "p" + i).ToList();

        Assert.ThrowsAny<ArgumentException>(() => new PlayerState(Tile, friends: many));
        Assert.Equal(Wire.MaxListEntries, new PlayerState(Tile, friends: many.Take(Wire.MaxListEntries)).Friends.Count);
    }

    // Reading a state.

    [Fact]
    public void ReadingRefusesWhatIsNotJsonOrNotAnObject()
    {
        Refuses("hello"u8.ToArray());
        Refuses(Json(new object?[] { 1, 2 }));
        Refuses([]);
        Refuses([0xff, 0xfe]);
    }

    [Fact]
    public void ReadingRefusesAMissingOddOrLaterVersion()
    {
        Refuses(Without("version"));
        Refuses(With("version", 0));
        Refuses(With("version", "1"));
        Refuses(With("version", PlayerState.Version + 1));
    }

    [Fact]
    public void ReadingSaysWhichVersionCameFromALaterServer()
    {
        Assert.Contains("later", Assert.ThrowsAny<FormatException>(() => PlayerState.Decode(With("version", 99))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingRefusesABadTile()
    {
        Refuses(Without("tile"));
        Refuses(With("tile", new object?[] { 1, 2 }));
        Refuses(With("tile", new object?[] { 1, 2, "a" }));
        Refuses(With("tile", new object?[] { 1, 2, 99 }));
        Refuses(With("tile", new object?[] { -1, 2, 0 }));
    }

    [Fact]
    public void ReadingRefusesASkillThatIsNotAWholeNumberOrIsNegative()
    {
        Refuses(With("skills", new Dictionary<string, object?> { ["a"] = "x" }));
        Refuses(With("skills", new Dictionary<string, object?> { ["a"] = 1.5 }));
        Refuses(With("skills", new Dictionary<string, object?> { ["a"] = -1 }));
        Refuses(With("skills", new object?[] { 1 }));
    }

    [Fact]
    public void ReadingRefusesAQuestStageThatIsNotAWholeNumber()
    {
        Refuses(With("quests", new Dictionary<string, object?> { ["a"] = "x" }));
        Refuses(With("quests", new Dictionary<string, object?> { ["a"] = -1 }));
        Refuses(With("quests", new object?[] { 1 }));
    }

    [Fact]
    public void ReadingASaveFromBeforeQuestsGivesNone()
    {
        Assert.Empty(PlayerState.Decode(Without("quests")).Quests);
    }

    [Fact]
    public void ReadingRefusesAnUnknownContainerAndABadSlot()
    {
        Refuses(With("containers", new Dictionary<string, object?> { ["chest"] = Array.Empty<object?>() }));
        object?[][] slots =
        [
            [-1, Coins, 1],
            [Container.InventorySize, Coins, 1],
            [0, 0, 1],
            [0, Coins, 0],
            [0, Coins, ItemStack.MaxCount + 1L],
            [0, Coins],
            ["0", Coins, 1],
        ];
        foreach (var slot in slots)
        {
            Refuses(With("containers", new Dictionary<string, object?> { ["inventory"] = new object?[] { slot } }));
        }
    }

    [Fact]
    public void ReadingRefusesASlotNamedTwice()
    {
        Refuses(With("containers", new Dictionary<string, object?>
        {
            ["inventory"] = new object?[] { new object?[] { 0, Coins, 1 }, new object?[] { 0, Logs, 1 } },
        }));
    }

    [Fact]
    public void ReadingRefusesListsThatHoldSomethingElseThanPlayerKeys()
    {
        Refuses(With("friends", new object?[] { 1 }));
        Refuses(With("friends", new object?[] { "Bo" }));
        Refuses(With("ignored", "bo"));
    }

    [Fact]
    public void ReadingTakesAStateWithOnlyTheVersionAndATile()
    {
        var back = PlayerState.Decode(Json(new Dictionary<string, object?> { ["version"] = 1, ["tile"] = new object?[] { 3, 4, 0 } }));

        Assert.Equal(new TileCoord(3, 4, 0), back.Tile);
        Assert.Empty(back.Quests);
    }

    // UTF-8 in .NET keeps a leading byte order mark, so the reader drops one.
    [Fact]
    public void ReadingDropsALeadingByteOrderMark()
    {
        Assert.Equal(new TileCoord(40, 41, 1), PlayerState.Decode([0xEF, 0xBB, 0xBF, .. Sample().Encode()]).Tile);
    }

    [Fact]
    public void RandomDamageNeverThrowsAnythingButAFormatException()
    {
        var random = new Random(11);
        var bytes = Sample().Encode();
        for (var i = 0; i < 3000; i++)
        {
            var damaged = bytes.ToArray();
            var hits = 1 + random.Next(4);
            for (var hit = 0; hit < hits; hit++)
            {
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
            }

            var cut = random.Next(2) == 0 ? damaged.Length : random.Next(damaged.Length);
            try
            {
                _ = PlayerState.Decode(damaged.AsSpan(0, cut));
            }
            catch (FormatException)
            {
                // Refused, which is the point.
            }
        }
    }
}
