/// Tile coordinates, flags, paths and the rules a server and a client share.
library;

export 'src/flag_region.dart' show FlagRegion;
export 'src/flag_region_file.dart'
    show decodeFlagRegion, encodeFlagRegion, flagRegionVersion;
export 'src/message_codec.dart'
    show
        clientMessageLimit,
        decodeClientMessage,
        decodeServerMessage,
        encodeClientMessage,
        encodeServerMessage;
export 'src/messages.dart'
    show
        Avatar,
        BadMessage,
        Chat,
        ClientMessage,
        Decoded,
        Intent,
        Interact,
        Login,
        LoginOk,
        LoginRefusal,
        Notice,
        PrivateState,
        ServerMessage,
        Understood,
        Unreadable,
        Walk,
        isValidName,
        isValidText,
        maxDeltaBytes,
        maxNameChars,
        maxTextBytes,
        nameKey;
export 'src/mover.dart' show MoveSpeed, Mover;
export 'src/path.dart'
    show PathResult, defaultPathMargin, findPath, findPathToReach;
export 'src/line_of_sight.dart' show hasClearLine;
export 'src/reach.dart'
    show Beside, Footprint, OnTile, Reach, Within, reachSatisfied;
export 'src/tile_coord.dart'
    show RegionKey, TileCoord, planeCount, regionSize, regionsAcross, worldSize;
export 'src/tile_flags.dart' show TileFlags;
export 'src/tile_position.dart'
    show
        TilePoint,
        readTilePoint,
        tileNearest,
        tilePositionFloats,
        tilePositionName,
        writeTilePosition;
export 'src/tile_map.dart' show TileMap, Walker;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeCoreVersion = '0.6.0';
