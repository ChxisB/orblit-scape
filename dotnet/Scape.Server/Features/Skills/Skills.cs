using Orblit.Progress;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// Experience in each skill, for every player in the world. Each player's experience is a
/// <see cref="ProgressTable"/>. A grant tells only that player which skills changed. The table is
/// lost when the player goes, until accounts save it.
/// </summary>
public sealed class Skills
{
    private readonly IHosting _hosting;
    private readonly SkillSet _set;
    private readonly Dictionary<string, ProgressTable> _tables = [];

    private Skills(IHosting hosting, SkillSet set)
    {
        _hosting = hosting;
        _set = set;
    }

    /// <summary>
    /// Adds skills to <paramref name="registry"/> and returns the feature, so gathering, making
    /// and combat can grant experience.
    /// </summary>
    public static Skills Register(Registry registry, Services services, SkillSet skills)
    {
        var feature = new Skills(services.Hosting, skills);
        registry.AddJoinHandler(feature.Join);
        registry.AddGoneHandler(feature.Gone);
        return feature;
    }

    /// <summary>The experience <paramref name="player"/> has in <paramref name="skill"/>, or zero for a player who is not in the world.</summary>
    /// <exception cref="ArgumentException">The skill does not exist.</exception>
    public long XpOf(string player, string skill)
    {
        var name = SkillSet.XpName(Def(skill).Id);
        return _tables.TryGetValue(player, out var table) ? table.Get(name) : 0;
    }

    /// <summary>The level of <paramref name="player"/> in <paramref name="skill"/>. A player who is not in the world is level 1.</summary>
    /// <exception cref="ArgumentException">The skill does not exist.</exception>
    public int LevelOf(string player, string skill) => _set.LevelOf(Def(skill), XpOf(player, skill));

    /// <summary>
    /// Adds <paramref name="xp"/> to <paramref name="player"/>'s <paramref name="skill"/>.
    /// Experience stops at the level cap. A player who is not in the world gains nothing.
    /// </summary>
    /// <returns>Each level reached, once each and lowest first.</returns>
    /// <exception cref="ArgumentException">The skill does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="xp"/> is negative.</exception>
    // F5: the levels reached are the caller's cue to announce them.
    public IReadOnlyList<int> Grant(string player, string skill, long xp)
    {
        var def = Def(skill);
        ArgumentOutOfRangeException.ThrowIfNegative(xp);
        if (!_tables.TryGetValue(player, out var table))
        {
            return [];
        }

        var name = SkillSet.XpName(def.Id);
        var before = table.Get(name);
        var max = _set.MaxXp(def);

        // Compared before adding, so a huge grant cannot overflow.
        var after = xp > max - before ? max : before + xp;
        if (after == before)
        {
            return [];
        }

        var (next, changes) = table.Set(name, after);
        _tables[player] = next;
        Send(player, changes);
        var from = _set.LevelOf(def, before) + 1;
        return [.. Enumerable.Range(from, _set.LevelOf(def, after) - from + 1)];
    }

    /// <summary>Grants <paramref name="xp"/> as <see cref="Grant"/> does and tells <paramref name="player"/> each level they reach.</summary>
    /// <returns>The levels reached.</returns>
    // F5: the caller needs to know whether a level came, to refit.
    public IReadOnlyList<int> Award(string player, string skill, long xp)
    {
        var reached = Grant(player, skill, xp);
        foreach (var level in reached)
        {
            _hosting.Tell(player, new Notice(FormattableString.Invariant($"You reached level {level} in {skill}.")));
        }

        return reached;
    }

    /// <summary>
    /// The experience of <paramref name="player"/> in each skill where it is above zero, by skill
    /// id. Empty for a player who is not in the world.
    /// </summary>
    public IReadOnlyDictionary<string, long> ExperienceOf(string player)
    {
        if (!_tables.TryGetValue(player, out var table))
        {
            return new Dictionary<string, long>();
        }

        return _set.Skills
            .Select(skill => (skill.Id, Xp: table.Get(SkillSet.XpName(skill.Id))))
            .Where(skill => skill.Xp > 0)
            .ToDictionary(skill => skill.Id, skill => skill.Xp);
    }

    /// <summary>
    /// Sets <paramref name="player"/>'s experience from <paramref name="saved"/> and tells them
    /// every skill that is above zero. A skill the game no longer has is left out, and experience
    /// stops at the level cap.
    /// </summary>
    /// <returns>How many saved skills were left out. A player who is not in the world leaves out all of them.</returns>
    // F5: the count is how the caller tells an operator what a save lost.
    public int Restore(string player, IReadOnlyDictionary<string, long> saved)
    {
        if (!_tables.TryGetValue(player, out var table))
        {
            return saved.Count;
        }

        var dropped = 0;
        foreach (var (id, xp) in saved)
        {
            if (_set.Find(id) is { } def)
            {
                table = table.Set(SkillSet.XpName(id), Math.Min(xp, _set.MaxXp(def))).Table;
            }
            else
            {
                dropped++;
            }
        }

        _tables[player] = table;
        var changes = table.ChangesSince(new ProgressTable(_set.Schema));
        if (!changes.IsEmpty)
        {
            Send(player, changes);
        }

        return dropped;
    }

    private SkillDef Def(string skill) =>
        _set.Find(skill) ?? throw new ArgumentException($"{skill} is not a skill.", nameof(skill));

    private void Send(string player, Changes changes) =>
        _hosting.Tell(player, new PrivateState([SkillSet.DeltaTag, .. changes.Encode()]));

    private void Join(string player) => _tables[player] = new ProgressTable(_set.Schema);

    private void Gone(string player) => _tables.Remove(player);
}
