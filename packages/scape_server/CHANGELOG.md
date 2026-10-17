# Changelog

## 0.11.0

- `Combat` runs melee. A click on a creature walks the player up and
  swings at the weapon's speed, and follows the creature if it moves.
  A creature that is hit, or an aggressive one that reaches its prey,
  swings back at its own speed.
- A creature that loses stays down until its respawn time and a player
  is near. A player who loses stands at the spawn tile with full health
  and everything they carried.
- Killing a creature grants fighting and health experience once.
  `Combat.addKillHandler` tells a feature of each kill.
- `Health` is replicated on every fighter. A health level raises the
  most hit points. Hit points come back one at a time after the fight.
- `Npcs.kill` removes a creature until it respawns. `Interactions` takes
  `npcs` and `addNpcAction`.
- `ScapeServer` takes `combat`.

## 0.10.0

- `Npcs` places creatures and moves them on the NPC phase. A wanderer
  stays inside its radius. An aggressive creature notices a player at its
  sight and follows until the player leaves its leash, then walks home.
- A creature only moves while a player is near its home. When its cell
  wakes it stands at home again.
- `ScapeServer` takes `npcs`. `Movement.tiles` lists where players
  stand.

## 0.9.0

- `Gathering` adds the `gather` object option. A player tries every
  `gatherTicks` until the bag is full, the node runs dry or they do
  something else. A dry node fills again on its tick, once a player is
  near it.
- `ScapeServer` takes `nodes`, and an optional `random` so a test can
  roll the dice.

## 0.8.0

- `WorldObjects` places doors, stairs and the like. A state change tells
  the clients near it and frees or blocks the object's tiles.
- `Interactions` walks a player to where they can reach a thing, then
  runs what the click names: an object option, a pick up from the ground,
  an item used on an object or an item used on an item. A walk or a new
  click cancels one that has not run. Features add their own actions with
  `addObjectAction`, `addUseOnObject` and `addUseOnItem`.
- `ScapeServer` takes the `objects` of the game.

## 0.7.0

- `Items` holds each player's inventory, equipment and bank. The owner is
  sent the slots that changed once a tick.
- `GroundItems` puts items on tiles. An item first belongs to the player
  who dropped it, `groundOwnerTicks`, and the other players are not told
  of it. Then it is open to all for `groundPublicTicks` and vanishes.
  `dropFrom` and `pickUp` return an `Outcome`.
- `ScapeServer` takes the `items` of the game.

## 0.6.0

- `Skills` holds each player's experience in a `ProgressTable`. `grant`
  returns each level reached, once each, and stops at the level cap. The
  player is sent only the skills that changed.
- `ScapeServer` takes the `skills` of the game.

## 0.5.0

- Public chat reaches the speaker and players within 15 tiles on the same
  plane, skipping any who ignore the speaker. The line names the speaker's
  avatar so a client can draw it over their head.
- A whisper goes to one player by name. An offline name and a name that
  ignores the sender get the same notice.
- `EditList` adds or removes a name on a friend or ignore list, up to 50
  each. The player is sent both lists, with which friends are online, on
  login, on every change and when a friend comes or goes.
- `ScapeServer.social` mutes and unmutes a player. A muted player is told
  so and nobody hears them. Lists and mutes last while the server runs.
- `Movement.standing` says which entity is a player's and where it stands.

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
