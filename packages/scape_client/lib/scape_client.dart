/// The client package of orblit-scape.
library;

export 'src/animator.dart' show ActorAnimator;
export 'src/menu.dart' show MenuEntry, Menus;
export 'src/minimap.dart' show Dot, DotKind, minimapDots;
export 'src/orbit_view.dart'
    show OrbitView, PickRay, maxDistance, maxPitch, minDistance, minPitch;
export 'src/session.dart'
    show
        Actor,
        Bubble,
        ChatTab,
        GameSession,
        GroundSprite,
        Health,
        Heard,
        JournalEntry,
        NpcSprite,
        ObjectSprite,
        Splat,
        Spoken,
        bubbleSeconds,
        interpolationDelay;
export 'src/tile_pick.dart' show pickTile, standingPoint;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeClientVersion = '0.16.0';
