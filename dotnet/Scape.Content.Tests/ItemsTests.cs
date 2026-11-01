using Scape.Core;

namespace Scape.Content.Tests;

public class ItemsTests
{
    [Fact]
    public void EveryNamedItemIsInTheSetUnderItsOwnId()
    {
        var named = new Dictionary<int, string>
        {
            [ItemId.Coins] = "Coins",
            [ItemId.Logs] = "Logs",
            [ItemId.CopperOre] = "Copper ore",
            [ItemId.BronzeBar] = "Bronze bar",
            [ItemId.RawFish] = "Raw fish",
            [ItemId.CookedFish] = "Cooked fish",
            [ItemId.BronzeAxe] = "Bronze axe",
            [ItemId.BronzePickaxe] = "Bronze pickaxe",
            [ItemId.BronzeSword] = "Bronze sword",
            [ItemId.WoodenShield] = "Wooden shield",
            [ItemId.BronzeHelmet] = "Bronze helmet",
            [ItemId.FishingNet] = "Fishing net",
            [ItemId.Bones] = "Bones",
            [ItemId.Feathers] = "Feathers",
            [ItemId.BurntFish] = "Burnt fish",
            [ItemId.Knife] = "Knife",
            [ItemId.Tinderbox] = "Tinderbox",
            [ItemId.ArrowShafts] = "Arrow shafts",
            [ItemId.Hammer] = "Hammer",
            [ItemId.Shortbow] = "Shortbow",
            [ItemId.BronzeArrows] = "Bronze arrows",
            [ItemId.Runes] = "Runes",
        };

        Assert.Equal(named.Count, DefaultItems.All.Items.Count);
        foreach (var (id, name) in named)
        {
            Assert.Equal(name, DefaultItems.All.Require(id).Name);
        }
    }

    [Fact]
    public void OnlyCoinsFeathersShaftsArrowsAndRunesStack()
    {
        Assert.Equal(
            [ItemId.Coins, ItemId.Feathers, ItemId.ArrowShafts, ItemId.BronzeArrows, ItemId.Runes],
            DefaultItems.All.Items.Where(item => item.Stackable).Select(item => item.Id));
    }

    [Fact]
    public void WornItemsNameASlotAndToolsNeedALevel()
    {
        Assert.Equal("head", DefaultItems.All.Require(ItemId.BronzeHelmet).EquipSlot);
        Assert.Equal("shield", DefaultItems.All.Require(ItemId.WoodenShield).EquipSlot);
        Assert.Equal("weapon", DefaultItems.All.Require(ItemId.BronzeSword).EquipSlot);
        Assert.NotNull(DefaultItems.All.Require(ItemId.BronzeAxe).Needs);
        Assert.Null(DefaultItems.All.Require(ItemId.Logs).EquipSlot);
    }

    [Fact]
    public void AnEquipmentContainerTakesTheHelmetAndRefusesTheLogs()
    {
        var worn = Container.ForId(ContainerId.Equipment, DefaultItems.All);

        Assert.Equal(1, worn.Add(ItemId.BronzeHelmet, 1));
        Assert.Equal(0, worn.Add(ItemId.Logs, 1));
    }
}
