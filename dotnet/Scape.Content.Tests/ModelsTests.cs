using Scape.Core;

namespace Scape.Content.Tests;

public class ModelsTests
{
    private static readonly ModelSet Models = DefaultModels.All;

    private static Look Dressed(string? slot = null, int item = 0) =>
        new(0xE0AC69, 0x3B2F2F, 0x2F6FDB, [.. ItemDef.EquipSlots.Select(s => s == slot ? item : 0)]);

    [Fact]
    public void EveryCreatureHasAModelWhoseNameCarriesAVersion()
    {
        foreach (var npc in DefaultNpcs.All.Npcs)
        {
            var model = Models.Npc(npc.Id);
            Assert.NotNull(model);
            Assert.Matches(@"^[a-z]+\.v\d+$", model.Name);
        }

        Assert.Equal(DefaultNpcs.All.Npcs.Select(n => n.Id).ToHashSet(), Models.NpcIds.ToHashSet());
    }

    [Fact]
    public void NoTwoCreaturesShareAModelName()
    {
        var names = Models.NpcIds.Select(id => Models.Npc(id)!.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void ACreatureWithNoModelIsNull()
    {
        Assert.Null(Models.Npc(9999));
    }

    [Fact]
    public void EveryModelHasAPivotForEachBoneItMoves()
    {
        ActorModel[] models = [.. Models.NpcIds.Select(id => Models.Npc(id)!), Models.Player(Dressed())];
        foreach (var model in models)
        {
            Assert.All(model.Boxes, box => Assert.True(model.Pivots.ContainsKey(box.Bone), model.Name));
        }
    }

    [Fact]
    public void ASmallerCreatureHasItsPivotsAtItsOwnSize()
    {
        var goblin = Models.Npc(NpcId.Goblin)!;
        var person = Models.Player(Dressed());

        Assert.Equal(person.Pivots[ModelBone.ArmR].Y * 0.8, goblin.Pivots[ModelBone.ArmR].Y, 1e-9);
    }

    // A player.

    [Fact]
    public void APlayerIsTheBodyAloneWithNothingWorn()
    {
        var model = Models.Player(Dressed());

        Assert.StartsWith("person.v1.", model.Name, StringComparison.Ordinal);
        Assert.Equal(7, model.Boxes.Count);
    }

    [Fact]
    public void APlayerShowsAPartForEachItemWornSoEquippingChangesTheModel()
    {
        var bare = Models.Player(Dressed());
        var armed = Models.Player(Dressed("weapon", ItemId.BronzeSword));

        Assert.Equal(bare.Boxes.Count + 1, armed.Boxes.Count);
        Assert.Equal(ModelBone.ArmR, armed.Boxes[^1].Bone);
    }

    [Fact]
    public void APlayerHasANameForEachLookSoACacheKeyedByNameIsRight()
    {
        var bare = Models.Player(Dressed());
        var armed = Models.Player(Dressed("weapon", ItemId.BronzeSword));
        var again = Models.Player(Dressed());

        Assert.NotEqual(bare.Name, armed.Name);
        Assert.Equal(bare.Name, again.Name);
    }

    [Fact]
    public void APlayerIsDrawnInTheColoursOfTheLook()
    {
        var model = Models.Player(Dressed());

        Assert.Contains(model.Boxes, box => box.Colour == 0xE0AC69);
        Assert.Contains(model.Boxes, box => box.Colour == 0x3B2F2F);
        Assert.Contains(model.Boxes, box => box.Colour == 0x2F6FDB);
    }

    [Fact]
    public void APlayerShowsAPairForLegsAndNothingForARing()
    {
        Assert.Equal(7 + 2, Models.Player(Dressed("legs", 3)).Boxes.Count);
        Assert.Equal(7, Models.Player(Dressed("ring", 3)).Boxes.Count);
    }

    [Fact]
    public void APlayerShowsTwoDifferentItemsInOneSlotDifferently()
    {
        var a = Models.Player(Dressed("weapon", 1));
        var b = Models.Player(Dressed("weapon", 2));

        Assert.NotEqual(a.Boxes[^1].Colour, b.Boxes[^1].Colour);
    }

    [Fact]
    public void APlayerHasAPartForEverySlotAnItemCanFillButTheRing()
    {
        foreach (var slot in ItemDef.EquipSlots.Where(s => s != "ring"))
        {
            Assert.True(Models.Player(Dressed(slot, 5)).Boxes.Count > 7, slot);
        }
    }
}
