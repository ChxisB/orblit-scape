using System.Diagnostics.CodeAnalysis;

namespace Scape.Server;

/// <summary>Which running actions a new one cancels.</summary>
public abstract class Cancels
{
    private protected Cancels()
    {
    }
}

public sealed class CancelsNothing : Cancels;

/// <summary>Every running action but those in <see cref="Except"/>.</summary>
public sealed class CancelsAll(IReadOnlySet<ActionKind>? except = null) : Cancels
{
    public IReadOnlySet<ActionKind> Except { get; } = except ?? new HashSet<ActionKind>();
}

public sealed class CancelsOnly(IReadOnlySet<ActionKind> kinds) : Cancels
{
    public IReadOnlySet<ActionKind> Kinds { get; } = kinds;
}

/// <summary>One thing an actor can be doing, such as walking or chopping a tree.</summary>
public sealed class ActionKind
{
    public ActionKind(string name, int delay = 0, Cancels? cancels = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        Name = name;
        Delay = delay;
        Cancels = cancels ?? new CancelsNothing();
    }

    public string Name { get; }

    /// <summary>Ticks between the intent and the action running.</summary>
    public int Delay { get; }

    public Cancels Cancels { get; }

    /// <summary>Whether starting this kind cancels an action of kind <paramref name="running"/>.</summary>
    public bool Interrupts(ActionKind running) => Cancels switch
    {
        CancelsNothing => false,
        CancelsAll all => !all.Except.Contains(running),
        CancelsOnly only => only.Kinds.Contains(running),
        _ => throw new InvalidOperationException($"{Cancels.GetType().Name} is not a cancel rule."),
    };
}

/// <summary>Actions waiting for their delay to pass, in the order they were queued.</summary>
/// <remarks>
/// The queue counts ticks itself, so a delay counts from the tick an action was submitted in,
/// whichever phase submitted it.
/// </remarks>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The game's term. An action leaves when it is due, so it is not a Queue<T>.")]
public sealed class ActionQueue
{
    private readonly List<Queued> _pending = [];

    public long Tick { get; private set; }

    /// <summary>How many actions of <paramref name="actor"/> are still waiting.</summary>
    public int Pending(ulong actor) => _pending.Count(q => q.Actor == actor);

    /// <summary>Starts the next tick.</summary>
    public void Advance() => Tick++;

    /// <summary>
    /// Queues <paramref name="run"/> for <paramref name="actor"/> after <paramref name="kind"/>'s
    /// delay, and cancels the actor's waiting actions that <paramref name="kind"/> interrupts.
    /// </summary>
    public void Submit(ulong actor, ActionKind kind, Action run)
    {
        _pending.RemoveAll(q => q.Actor == actor && kind.Interrupts(q.Kind));
        _pending.Add(new Queued(actor, kind, Tick + kind.Delay, run));
    }

    /// <summary>
    /// Runs every action that is due, in the order they were queued. One queued by an action that
    /// runs now waits for the next tick.
    /// </summary>
    public void RunDue()
    {
        foreach (var queued in _pending.ToArray())
        {
            if (queued.DueTick > Tick || !_pending.Remove(queued))
            {
                continue;
            }

            queued.Run();
        }
    }

    // A class, not a record, so a remove finds this entry and no equal one.
    private sealed class Queued(ulong actor, ActionKind kind, long dueTick, Action run)
    {
        public ulong Actor { get; } = actor;

        public ActionKind Kind { get; } = kind;

        public long DueTick { get; } = dueTick;

        public Action Run { get; } = run;
    }
}
