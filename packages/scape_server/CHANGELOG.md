# Changelog

## 0.1.0

- `dart run scape_server` runs a tick every 600 ms on a `TickClock`. A late
  tick logs where the time went.
- A tick runs six phases in order: inbound, actions, npcs, movement, combat
  and outbound. The host then publishes. Each phase is timed by the host.
- `ActionQueue` holds actions with a delay in ticks. A new action cancels the
  actor's waiting actions that its `Cancels` rule names. Actions run in the
  order they were queued.
- `Registry` takes the action kinds and per-phase systems that features add.
  `lib/src/features.dart` is the one place features join, and none do yet.
- A version constant and a test that keeps it equal to `pubspec.yaml`.
