using Orblit.Core;
using Scape.Core;

namespace Scape.Server;

/// <summary>
/// What the features add at startup: action kinds, the systems that run in each phase, the
/// components they replicate and the handlers for intents and joins. The engine reads it and
/// knows no feature.
/// </summary>
public sealed class Registry
{
    private readonly Dictionary<string, ActionKind> _kinds = [];
    private readonly Dictionary<Phase, List<Action>> _systems =
        Enum.GetValues<Phase>().ToDictionary(phase => phase, _ => new List<Action>());

    private readonly List<ComponentType> _replicated = [];
    private readonly List<Action<string, Intent>> _intentHandlers = [];
    private readonly List<Action<string>> _joinHandlers = [];
    private readonly List<Action<string>> _goneHandlers = [];

    public ActionQueue Actions { get; } = new();

    /// <summary>The components the host sends to clients, in the order they were added.</summary>
    public IReadOnlyList<ComponentType> Replicated => [.. _replicated];

    public void Replicate(ComponentType component) => _replicated.Add(component);

    public void AddIntentHandler(Action<string, Intent> handler) => _intentHandlers.Add(handler);

    public void AddJoinHandler(Action<string> handler) => _joinHandlers.Add(handler);

    public void AddGoneHandler(Action<string> handler) => _goneHandlers.Add(handler);

    /// <summary>Offers an intent to every handler. Each one picks out what it owns.</summary>
    public void Deliver(string player, Intent intent)
    {
        foreach (var handler in _intentHandlers)
        {
            handler(player, intent);
        }
    }

    public void PlayerJoined(string player)
    {
        foreach (var handler in _joinHandlers)
        {
            handler(player);
        }
    }

    public void PlayerGone(string player)
    {
        foreach (var handler in _goneHandlers)
        {
            handler(player);
        }
    }

    public void AddKind(ActionKind kind)
    {
        if (!_kinds.TryAdd(kind.Name, kind))
        {
            throw new ArgumentException($"{kind.Name} is already registered.", nameof(kind));
        }
    }

    public ActionKind? KindNamed(string name) => _kinds.GetValueOrDefault(name);

    public void AddSystem(Phase phase, Action system) => _systems[phase].Add(system);

    public IReadOnlyList<Action> SystemsFor(Phase phase) => _systems[phase];
}
