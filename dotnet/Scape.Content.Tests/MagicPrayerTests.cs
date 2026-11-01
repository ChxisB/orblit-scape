using Scape.Core;

namespace Scape.Content.Tests;

public class MagicPrayerTests
{
    [Fact]
    public void TheBowIsRangedAndTheArrowsFitTheAmmoSlot()
    {
        var bow = DefaultItems.All.Require(ItemId.Shortbow).Combat!;
        var arrows = DefaultItems.All.Require(ItemId.BronzeArrows);

        Assert.Equal(CombatStyle.Ranged, bow.Style);
        Assert.Equal(bow.Ammo, arrows.Combat!.Ammo);
        Assert.Equal("ammo", arrows.EquipSlot);
        Assert.True(arrows.Stackable);
    }

    [Fact]
    public void EverySpellAndPrayerIsFoundByItsId()
    {
        Assert.IsType<SpellDamage>(DefaultSpells.All.Find(SpellId.Strike)!.Effect);
        Assert.IsType<SpellStun>(DefaultSpells.All.Find(SpellId.Daze)!.Effect);
        Assert.IsType<SpellTeleport>(DefaultSpells.All.Find(SpellId.Home)!.Effect);
        Assert.Equal(CombatStat.Defence, DefaultPrayers.All.Find(PrayerId.ThickSkin)!.Stat);
        Assert.Equal(3, DefaultPrayers.All.Prayers.Count);
    }

    [Fact]
    public void APrayerThatDrainsFiveATickSpendsAPointInTwentyTicks()
    {
        var drain = DefaultPrayers.All.Find(PrayerId.ThickSkin)!.Drain;

        Assert.Equal(20, DefaultPrayers.All.DrainPerPoint / drain);
    }
}
