import 'package:orblit_net/orblit_net.dart';

import 'phase.dart';
import 'registry.dart';

/// Waits until the next tick is due. Declared here so a test can move time by
/// hand (F10).
abstract interface class Pacer {
  Future<void> untilNext();
}

/// Waits on a [TickClock]. A wait of zero still yields to the event loop, so
/// sockets are served between ticks.
final class ClockPacer implements Pacer {
  const ClockPacer(this._clock);

  final TickClock _clock;

  @override
  Future<void> untilNext() => Future<void>.delayed(_clock.untilNext());
}

/// Runs the world one tick at a time on one isolate.
final class TickLoop {
  TickLoop({required this.host, required this.registry, required Pacer pacer})
    : _pacer = pacer;

  final NetHost host;
  final Registry registry;
  final Pacer _pacer;

  bool _stopped = false;

  int get tick => registry.actions.tick;

  /// Ticks until [stop] is called.
  Future<void> run() async {
    while (!_stopped) {
      await _pacer.untilNext();
      if (_stopped) return;
      runTick();
    }
  }

  void stop() => _stopped = true;

  void runTick() {
    registry.actions.advance();
    for (final phase in Phase.values) {
      host.phase(phase.name, () => _runPhase(phase));
    }
    host.publish();
  }

  void _runPhase(Phase phase) {
    if (phase == Phase.actions) registry.actions.runDue();
    for (final system in registry.systemsFor(phase)) {
      system();
    }
  }
}
