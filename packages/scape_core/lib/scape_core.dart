/// Tile coordinates, flags, paths and the rules a server and a client share.
library;

export 'src/flag_region.dart' show FlagRegion;
export 'src/flag_region_file.dart'
    show decodeFlagRegion, encodeFlagRegion, flagRegionVersion;
export 'src/tile_coord.dart'
    show RegionKey, TileCoord, planeCount, regionSize, regionsAcross, worldSize;
export 'src/tile_flags.dart' show TileFlags;
export 'src/tile_map.dart' show Mover, TileMap;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeCoreVersion = '0.2.0';
