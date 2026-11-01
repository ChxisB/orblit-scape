using Scape.Core;

namespace Scape.Content;

/// <summary>The ids of the spells in <see cref="DefaultSpells"/>.</summary>
public static class SpellId
{
    public const int Strike = 1;
    public const int Daze = 2;
    public const int Home = 3;
}

/// <summary>
/// The spells of the first game. Each costs runes. Strike hurts, daze keeps a
/// creature from swinging for a few ticks, and home puts the caster back at
/// the first tile of the world.
/// </summary>
public static class DefaultSpells
{
    public static SpellSet All { get; } = new(
        "magic",
        [
            new SpellDef(SpellId.Strike, "Strike", level: 1, new SpellDamage(4), cost: [new ItemStack(ItemId.Runes, 1)], xp: 6),
            new SpellDef(SpellId.Daze, "Daze", level: 5, new SpellStun(6), cost: [new ItemStack(ItemId.Runes, 2)], speed: 6, xp: 10),
            new SpellDef(
                SpellId.Home,
                "Home teleport",
                level: 10,
                new SpellTeleport(new TileCoord(32, 32, 0)),
                cost: [new ItemStack(ItemId.Runes, 3)],
                xp: 20),
        ],
        DefaultItems.All,
        DefaultSkills.All);
}
