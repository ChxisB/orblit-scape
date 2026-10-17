/// Which running actions a new one cancels.
sealed class Cancels {
  const Cancels();
}

final class CancelsNothing extends Cancels {
  const CancelsNothing();
}

/// Every running action but those in [except].
final class CancelsAll extends Cancels {
  const CancelsAll({this.except = const {}});

  final Set<ActionKind> except;
}

final class CancelsOnly extends Cancels {
  const CancelsOnly(this.kinds);

  final Set<ActionKind> kinds;
}

/// One thing an actor can be doing, such as walking or chopping a tree.
final class ActionKind {
  ActionKind(
    this.name, {
    this.delay = 0,
    this.cancels = const CancelsNothing(),
  }) {
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (delay < 0) throw ArgumentError.value(delay, 'delay', 'is below zero');
  }

  final String name;

  /// Ticks between the intent and the action running.
  final int delay;

  final Cancels cancels;

  /// Whether starting this kind cancels an action of kind [running].
  bool interrupts(ActionKind running) => switch (cancels) {
    CancelsNothing() => false,
    CancelsAll(:final except) => !except.contains(running),
    CancelsOnly(:final kinds) => kinds.contains(running),
  };
}

final class _Queued {
  _Queued(this.actor, this.kind, this.dueTick, this.run);

  final int actor;
  final ActionKind kind;
  final int dueTick;
  final void Function() run;
}

/// Actions waiting for their delay to pass, in the order they were queued.
///
/// The queue counts ticks itself, so a delay counts from the tick an action
/// was submitted in, whichever phase submitted it.
final class ActionQueue {
  final List<_Queued> _pending = [];
  int _tick = 0;

  int get tick => _tick;

  /// How many actions of [actor] are still waiting.
  int pending(int actor) => _pending.where((q) => q.actor == actor).length;

  /// Starts the next tick.
  void advance() => _tick++;

  /// Queues [run] for [actor] after [kind]'s delay, and cancels the actor's
  /// waiting actions that [kind] interrupts.
  void submit(int actor, ActionKind kind, void Function() run) {
    _pending.removeWhere((q) => q.actor == actor && kind.interrupts(q.kind));
    _pending.add(_Queued(actor, kind, _tick + kind.delay, run));
  }

  /// Runs every action that is due, in the order they were queued. One queued
  /// by an action that runs now waits for the next tick.
  void runDue() {
    for (final queued in List.of(_pending)) {
      if (queued.dueTick > _tick || !_pending.remove(queued)) continue;
      queued.run();
    }
  }
}
