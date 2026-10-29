using Scape.Core;

namespace Scape.Server;

/// <summary>What magic asks of the rest of the server.</summary>
public sealed record MagicOptions
{
    public required SpellSet Spells { get; init; }

    public required Skills Skills { get; init; }

    public required Items Bags { get; init; }

    /// <summary>Carries out a cast, since a spell lands as a hit or a stun.</summary>
    public required Combat Combat { get; init; }

    public required Movement Movement { get; init; }

    public required Npcs Npcs { get; init; }
}

/// <summary>
/// Spells a player casts by name at a creature in range with a clear line. A cast costs items and
/// a wait, and gives experience in the magic skill. A spell that is refused costs nothing, and
/// none of them walks the caster anywhere.
/// </summary>
public sealed class Magic
{
    private readonly Services _services;
    private readonly MagicOptions _options;

    private Magic(Services services, MagicOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>Adds spells to <paramref name="registry"/>.</summary>
    public static void Register(Registry registry, Services services, MagicOptions options) =>
        registry.AddIntentHandler(new Magic(services, options).OnIntent);

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    private void OnIntent(string player, Intent intent)
    {
        if (intent is not Cast cast || _options.Spells.Find(cast.Spell) is not { } spell)
        {
            return;
        }

        var target = _options.Npcs.Find((ulong)cast.Target);
        if (Refusal(player, spell, target) is { } refusal)
        {
            Tell(player, refusal);
            return;
        }

        // Refusal found them in the world, so they have an inventory.
        var inventory = _options.Bags.ContainerOf(player, ContainerId.Inventory)!;
        if (inventory.Exchange(take: spell.Cost) != Outcome.Done)
        {
            Tell(player, "You do not have what that spell costs.");
            return;
        }

        var skill = _options.Spells.Skill;
        _options.Skills.Award(player, skill, spell.Xp);
        _options.Combat.Cast(player, spell, _options.Skills.LevelOf(player, skill), target);
    }

    /// <summary>Why <paramref name="player"/> may not cast <paramref name="spell"/> at <paramref name="target"/> now, or null.</summary>
    private string? Refusal(string player, SpellDef spell, PlacedNpc? target)
    {
        if (_options.Skills.LevelOf(player, _options.Spells.Skill) < spell.Level)
        {
            return FormattableString.Invariant($"You need level {spell.Level} in {_options.Spells.Skill}.");
        }

        if (!_options.Combat.IsReady(player))
        {
            return "You are not ready yet.";
        }

        if (spell.Effect is SpellTeleport)
        {
            return null;
        }

        if (target?.Def.Fight is null || target.IsDead)
        {
            return "You cannot cast that there.";
        }

        var tile = _options.Movement.TileOf(player)!.Value; // IsReady found them in the world.
        var reach = new Within(Footprint.OfTile(target.Tile), spell.Range);
        return reach.IsSatisfied(_services.Map, tile) ? null : "That is out of range.";
    }
}
