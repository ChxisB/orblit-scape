# Changelog

## 0.4.0

- A client is sent the players in its own 16 tile cell and the ring of cells
  round it. A player who walks out of the ring is removed from its view.
- `Services.active` and `ScapeServer.active` say which cells are near a
  player. A cell stays active for 10 ticks after the last player leaves it.
  Nothing uses it until there are NPCs.
- `ScapeServer.listen` accepts WebSocket connections. The binary takes a port
  and an address, and listens on `127.0.0.1:7600` by default.
- A dropped player's avatar stays for 10 ticks, 6 seconds, instead of 600.

## 0.3.0

- Players walk. A login puts an avatar on the spawn tile and tells the client
  which entity it is. A walk intent finds a path on the action tick. The
  movement phase takes one step a tick and writes `TilePosition`, which is
  replicated.
- `ScapeServer` takes the tile map and the spawn tile.
- Features reach the host through `Services`. `Registry` holds their
  replicated components and their handlers for intents and joins.
- `tickPeriod` now comes from `scape_core`, so the client reads the same one.

## 0.2.0

- A client connects, logs in by name and sends intents. `Wire` decodes what
  clients send. A login takes any name nobody is logged in under, and the
  client is told the name it is known by.
- Walks, interactions and chat go into `Inbox`, which keeps at most 20 waiting
  for each player. The inbound phase drains it.
- Bytes that are not a message are refused and counted by why. A message over
  the host's budget never reaches the decoder, and the host counts it.

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
