/// The client package of orblit-scape.
library;

export 'src/orbit_view.dart'
    show OrbitView, PickRay, maxDistance, maxPitch, minDistance, minPitch;
export 'src/session.dart' show Actor, GameSession, interpolationDelay;
export 'src/tile_pick.dart' show pickTile, standingPoint;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeClientVersion = '0.3.0';
