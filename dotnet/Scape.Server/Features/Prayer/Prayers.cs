using Scape.Core;

namespace Scape.Server;

/// <summary>
/// Prayers a player switches on for a bonus to their fighting. Each one drains the prayer points a
/// player has, which equal their prayer level when they log in, and every prayer goes off when
/// the points reach zero. One prayer is on for each stat, and switching on another for the same
/// stat replaces it.
/// </summary>
public sealed class Prayers
{
    private readonly Services _services;
    private readonly PrayerSet _set;
    private readonly Skills _skills;
    private readonly Dictionary<string, Pool> _pools = [];

    private Prayers(Services services, PrayerSet set, Skills skills)
    {
        _services = services;
        _set = set;
        _skills = skills;
    }

    /// <summary>Adds prayers to <paramref name="registry"/> and returns the feature, so combat can ask for the bonus.</summary>
    public static Prayers Register(Registry registry, Services services, PrayerSet prayers, Skills skills)
    {
        var feature = new Prayers(services, prayers, skills);
        registry.AddIntentHandler(feature.OnIntent);
        registry.AddJoinHandler(feature.Join);
        registry.AddGoneHandler(player => feature._pools.Remove(player));
        registry.AddSystem(Phase.Combat, feature.Drain);
        return feature;
    }

    /// <summary>The percent the prayer that is on for <paramref name="stat"/> adds to the level of <paramref name="player"/> in it, or zero.</summary>
    public int PercentOf(string player, CombatStat stat) =>
        Active(player).FirstOrDefault(prayer => prayer.Stat == stat)?.Percent ?? 0;

    private IEnumerable<PrayerDef> Active(string player) =>
        (_pools.GetValueOrDefault(player)?.Active ?? []).Select(_set.Find).OfType<PrayerDef>();

    private int Max(string player) => _skills.LevelOf(player, _set.Skill);

    private int Points(string player, Pool pool) => Math.Max(0, Max(player) - pool.Lost);

    private void Tell(string player, string text) => _services.Hosting.Tell(player, new Notice(text));

    private void Join(string player)
    {
        _pools[player] = new Pool();
        Show(player);
    }

    private void OnIntent(string player, Intent intent)
    {
        if (intent is not TogglePrayer toggle
            || !_pools.TryGetValue(player, out var pool)
            || _set.Find(toggle.Prayer) is not { } prayer)
        {
            return;
        }

        if (!pool.Active.Remove(prayer.Id))
        {
            if (Refusal(player, pool, prayer) is { } refusal)
            {
                Tell(player, refusal);
                return;
            }

            pool.Active.RemoveAll(id => _set.Find(id)?.Stat == prayer.Stat);
            pool.Active.Add(prayer.Id);
        }

        Show(player);
    }

    private string? Refusal(string player, Pool pool, PrayerDef prayer)
    {
        if (_skills.LevelOf(player, _set.Skill) < prayer.Level)
        {
            return FormattableString.Invariant($"You need level {prayer.Level} in {_set.Skill}.");
        }

        return Points(player, pool) < 1 ? "You have no prayer points." : null;
    }

    /// <summary>
    /// Spends the drain of every prayer that is on. A prayer that runs the points out switches
    /// them all off. A player whose level changed since they were last told, by a login, a quest
    /// or a level, is told again.
    /// </summary>
    private void Drain()
    {
        foreach (var (player, pool) in _pools)
        {
            pool.Drained += Active(player).Sum(prayer => prayer.Drain);
            var spent = pool.Drained / _set.DrainPerPoint;
            pool.Drained -= spent * _set.DrainPerPoint;
            pool.Lost += spent;
            if (spent > 0 && Points(player, pool) == 0)
            {
                RunOut(player, pool);
            }

            if (spent > 0 || pool.ShownMax != Max(player))
            {
                Show(player);
            }
        }
    }

    private void RunOut(string player, Pool pool)
    {
        pool.Active.Clear();
        pool.Drained = 0;
        Tell(player, "You have run out of prayer points.");
    }

    private void Show(string player)
    {
        var pool = _pools[player];
        pool.ShownMax = Max(player);
        _services.Hosting.Tell(player, new PrayerView(Points(player, pool), pool.ShownMax, [.. pool.Active]));
    }

    /// <summary>What one player has done with their prayer points.</summary>
    private sealed class Pool
    {
        /// <summary>Points spent. The points left are the prayer level less this.</summary>
        public int Lost { get; set; }

        /// <summary>Drain that has not yet made a whole point.</summary>
        public int Drained { get; set; }

        /// <summary>The prayers that are on, in the order they were switched on.</summary>
        public List<int> Active { get; } = [];

        /// <summary>The most points the player was last told they have.</summary>
        public int ShownMax { get; set; }
    }
}
