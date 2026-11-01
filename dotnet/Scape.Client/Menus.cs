using Scape.Core;

namespace Scape.Client;

/// <summary>One line of the right-click menu. <paramref name="Run"/> sends what the line does.</summary>
public sealed record MenuEntry(string Label, Action Run);

/// <summary>
/// What a click on something in view offers, built from the content the game was made with. The
/// first entry is the click that needs no menu, and its label is the hover text.
/// </summary>
/// <remarks>The menu of a tile, Walk here and Cancel belong to the panel that draws it.</remarks>
public sealed class Menus(GameSession session, NpcSet npcs, ObjectSet objects, ItemSet items)
{
    /// <summary>Attack, Trade and Talk, for the ones this creature has.</summary>
    public IReadOnlyList<MenuEntry> OnNpc(NpcSprite sprite)
    {
        if (npcs.Find(sprite.Def) is not { } def)
        {
            return [];
        }

        var id = sprite.NetworkId;
        var entries = new List<MenuEntry>();
        if (def.Fight is not null)
        {
            entries.Add(new MenuEntry($"Attack {def.Name}", () => session.Attack(id)));
        }

        if (def.Shop is not null)
        {
            entries.Add(new MenuEntry($"Trade {def.Name}", () => session.Trade(id)));
        }

        if (def.Dialogue is not null)
        {
            entries.Add(new MenuEntry($"Talk to {def.Name}", () => session.Talk(id)));
        }

        return entries;
    }

    /// <summary>The options this object offers in the state it is in now.</summary>
    public IReadOnlyList<MenuEntry> OnObject(ObjectSprite sprite)
    {
        if (objects.Find(sprite.Def) is not { } def)
        {
            return [];
        }

        return
        [
            .. def.Options
                .Select((option, index) => (option, index))
                .Where(pair => pair.option.IsOfferedIn(sprite.State))
                .Select(pair => new MenuEntry($"{pair.option.Label} {def.Name}", () => session.UseObject(sprite.NetworkId, pair.index))),
        ];
    }

    public IReadOnlyList<MenuEntry> OnGround(GroundSprite sprite) =>
        items.Find(sprite.Item) is { } def
            ? [new MenuEntry($"Take {def.Name}", () => session.TakeItem(sprite.NetworkId))]
            : [];

    public IReadOnlyList<MenuEntry> OnPlayer(ActorSprite actor) =>
        actor.Mine ? [] : [new MenuEntry("Trade player", () => session.RequestTrade(actor.NetworkId))];
}
