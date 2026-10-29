namespace Scape.Core.Tests;

public class ObjectsTests
{
    private static ObjectOption Option(string action = "open", int? whenState = null) => new("Open", action, whenState);

    // Options.

    [Fact]
    public void WithNoStateIsOfferedInEveryState()
    {
        Assert.True(Option().IsOfferedIn(0));
        Assert.True(Option().IsOfferedIn(7));
    }

    [Fact]
    public void WithAStateIsOfferedOnlyInThatState()
    {
        var shut = Option("open", 0);

        Assert.True(shut.IsOfferedIn(0));
        Assert.False(shut.IsOfferedIn(1));
    }

    [Fact]
    public void RefusesAnEmptyLabelOrAction()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ObjectOption("", "a"));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectOption("a", ""));
    }

    // Definitions.

    [Fact]
    public void BlocksItsTilesInState0OnlyAndOnlyWhenSolid()
    {
        var tree = new ObjectDef(id: 1, name: "Tree", solid: true);
        var sign = new ObjectDef(id: 2, name: "Sign");

        Assert.True(tree.BlocksIn(0));
        Assert.False(tree.BlocksIn(1));
        Assert.False(sign.BlocksIn(0));
    }

    [Fact]
    public void GivesAFootprintOfItsSize()
    {
        var hall = new ObjectDef(id: 1, name: "Hall", width: 3, height: 2);

        var footprint = hall.FootprintAt(new TileCoord(10, 20, 0));

        Assert.Equal(6, footprint.Tiles.Count());
        Assert.Equal(new TileCoord(10, 20, 0), footprint.Origin);
    }

    [Fact]
    public void KeepsItsOptionsOutOfReachOfTheCaller()
    {
        var list = new List<ObjectOption> { Option() };
        var def = new ObjectDef(id: 1, name: "Door", options: list);

        list.Clear();

        Assert.Single(def.Options);
        Assert.Throws<NotSupportedException>(() => ((ICollection<ObjectOption>)def.Options).Add(Option()));
    }

    [Fact]
    public void RefusesAnIdNameSizeOrOptionCountOutOfRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: 0, name: "A"));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: ObjectDef.MaxId + 1, name: "A"));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: 1, name: ""));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: 1, name: "A", width: 0));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: 1, name: "A", height: 0));
        Assert.ThrowsAny<ArgumentException>(() => new ObjectDef(id: 1, name: "A", options: [.. Enumerable.Repeat(Option(), 256)]));
    }

    // The set.

    [Fact]
    public void FindsAndRequiresById()
    {
        var set = new ObjectSet([new ObjectDef(id: 3, name: "Door")]);

        Assert.Equal("Door", set.Find(3)?.Name);
        Assert.Null(set.Find(4));
        Assert.Equal("Door", set.Require(3).Name);
        Assert.ThrowsAny<ArgumentException>(() => set.Require(4));
    }

    [Fact]
    public void RefusesARepeatedId() =>
        Assert.ThrowsAny<ArgumentException>(() => new ObjectSet([new ObjectDef(id: 1, name: "A"), new ObjectDef(id: 1, name: "B")]));

    [Fact]
    public void TileFlagsWithoutClearsOnlyTheBitsNamed()
    {
        var flags = TileFlags.Blocked | TileFlags.WallNorth;

        Assert.False(flags.Without(TileFlags.Blocked).Has(TileFlags.Blocked));
        Assert.True(flags.Without(TileFlags.Blocked).Has(TileFlags.WallNorth));
        Assert.Equal(TileFlags.Open, TileFlags.Open.Without(TileFlags.Blocked));
    }

    [Fact]
    public void TheInteractKindsAreDistinctBytes()
    {
        int[] kinds = [InteractKind.PlacedObject, InteractKind.Ground, InteractKind.UseOnObject, InteractKind.UseOnItem];

        Assert.Equal(4, kinds.Distinct().Count());
        Assert.All(kinds, kind => Assert.InRange(kind, 0, 0xFF));
    }
}
