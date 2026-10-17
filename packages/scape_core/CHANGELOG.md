# Changelog

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
