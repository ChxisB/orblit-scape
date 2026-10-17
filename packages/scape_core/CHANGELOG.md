# Changelog

## 0.8.0

- `SkillDef`, `XpCurve` and `SkillSet` say what a skill is and what level
  an amount of experience gives. The curve uses whole numbers, so a server
  and a client agree on every platform.
- `skillsDeltaTag` starts a `PrivateState` delta that carries skill changes.
- Depends on `orblit_progress`.

## 0.7.0

- `Whisper`, `EditList` and `ListKind` are client messages. `Said`,
  `Whispered` and `SocialLists` are server messages. Each decoder returns
  `Unreadable` for a bad body and never throws.
- `maxListEntries` caps a friend or ignore list at 50 names.

## 0.6.0

- `TilePosition` is named once: its component name, its layout and the
  functions that write and read it, so a client and a server cannot disagree
  about which float is which.
- `tickPeriod` moved here from `scape_server`, since a client blends over it.

## 0.5.0

- `Avatar` tells a client which replicated entity is its own, by network id.

## 0.4.0

- Messages a client and a server send each other, named once: `Login`,
  `Walk`, `Interact` and `Chat` one way, `LoginOk`, `PrivateState` and
  `Notice` the other. A login refusal is a `LoginRefusal`, sent as the text
  of a goodbye.
- Each message has a size cap. `decodeClientMessage` and
  `decodeServerMessage` return `Understood` or `Unreadable` with a reason and
  never throw. A test feeds them 20,000 random messages.

## 0.3.0

- `findPath` and `findPathToReach`: the shortest walk over the flags, a
  diagonal counting as one step, inside a box round the start and the
  target. A target that cannot be reached gives the nearest reachable tile
  and says so.
- `Mover` holds a queue of steps. It walks a tile a tick and runs two, and a
  step the map no longer allows ends the walk.
- Reach rules: `OnTile`, `Beside` a footprint and `Within` a range of one,
  with `hasClearLine` for the range.
- The enum for who is stepping is `Walker`.

## 0.2.0

- Tiles: `TileCoord`, `RegionKey` and `TileFlags`. A region is 64 by 64
  tiles of one plane, and the world is 256 regions across.
- `TileMap.canStep` answers whether one step in eight directions is
  allowed. A diagonal needs both corner tiles open and no wall round them.
- `FlagRegion` and its file: a magic, a version, the key, then one byte a
  tile. A reader refuses a version it does not know.

## 0.1.0

- The package exists. It holds a version constant and a test that keeps it
  equal to the version in `pubspec.yaml`.
