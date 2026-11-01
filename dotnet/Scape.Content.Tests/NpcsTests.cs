using Scape.Core;

namespace Scape.Content.Tests;

public class NpcsTests
{
    [Fact]
    public void EachCreatureIdNamesOneCreature()
    {
        Assert.All(
            [NpcId.Chicken, NpcId.Goblin, NpcId.Guard, NpcId.Shopkeeper, NpcId.Lamplighter],
            id => Assert.NotNull(DefaultNpcs.All.Find(id)));
        Assert.Equal(5, DefaultNpcs.All.Npcs.Count);
    }

    [Fact]
    public void TheCreaturesBehaveAsTheirNamesSay()
    {
        Assert.IsType<Wander>(DefaultNpcs.All.Require(NpcId.Chicken).Behaviour);
        Assert.IsType<Idle>(DefaultNpcs.All.Require(NpcId.Guard).Behaviour);
        Assert.IsType<Aggressive>(DefaultNpcs.All.Require(NpcId.Goblin).Behaviour);
    }

    [Fact]
    public void EveryItemACreatureDropsOrSellsIsInTheItemSet()
    {
        foreach (var npc in DefaultNpcs.All.Npcs)
        {
            Assert.All(npc.Loot?.Items ?? [], item => Assert.NotNull(DefaultItems.All.Find(item)));
            Assert.All(npc.Shop?.Lines ?? [], line => Assert.NotNull(DefaultItems.All.Find(line.Item)));
        }
    }

    [Fact]
    public void TheFightersDropBonesAndTheShopkeeperDoesNotFight()
    {
        foreach (var id in new[] { NpcId.Chicken, NpcId.Goblin })
        {
            Assert.Equal([new ItemStack(ItemId.Bones, 1)], DefaultNpcs.All.Require(id).Loot!.Always);
        }

        Assert.Null(DefaultNpcs.All.Require(NpcId.Shopkeeper).Fight);
        Assert.NotNull(DefaultNpcs.All.Require(NpcId.Shopkeeper).Shop);
    }

    [Fact]
    public void AShopNeverPaysMoreForAnItemThanItSellsItFor()
    {
        foreach (var line in DefaultNpcs.All.Require(NpcId.Shopkeeper).Shop!.Lines)
        {
            var item = DefaultItems.All.Require(line.Item);

            Assert.True(DefaultShops.Rules.SellPrice(item) <= DefaultShops.Rules.BuyPrice(item));
        }
    }
}
