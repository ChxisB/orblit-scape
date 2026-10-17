/// The server package of orblit-scape.
library;

export 'src/engine/action.dart'
    show
        ActionKind,
        ActionQueue,
        Cancels,
        CancelsAll,
        CancelsNothing,
        CancelsOnly;
export 'src/engine/phase.dart' show Phase;
export 'src/engine/registry.dart' show IntentHandler, Registry;
export 'src/engine/services.dart' show Hosting, Services;
export 'src/engine/tick_loop.dart' show ClockPacer, Pacer, TickLoop;
export 'src/features/movement/movement.dart' show Movement;
export 'src/inbox.dart' show Inbox, PlayerIntent;
export 'src/server.dart' show ScapeServer;
export 'src/wire.dart' show Wire, WireLink;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeServerVersion = '0.3.0';

/// The time between two ticks.
const tickPeriod = Duration(milliseconds: 600);
