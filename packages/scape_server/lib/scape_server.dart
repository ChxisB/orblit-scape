/// The server package of orblit-scape.
library;

export 'src/accounts/password_hasher.dart' show Argon2idHasher, PasswordHasher;
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
export 'src/features/combat/combat.dart' show Combat, KillHandler;
export 'src/features/gathering/gathering.dart' show gatherTicks;
export 'src/features/interactions/interactions.dart'
    show Interactions, NpcAction, ObjectAction, UseOnItem, UseOnObject;
export 'src/features/items/ground.dart'
    show
        GroundItems,
        InventoryOf,
        Lying,
        TileOf,
        groundOwnerTicks,
        groundPublicTicks;
export 'src/features/items/items.dart' show Items;
export 'src/features/movement/movement.dart' show Movement;
export 'src/features/npcs/npcs.dart'
    show Npcs, PlacedNpc, PlayerTiles, wanderChance;
export 'src/features/objects/objects.dart' show PlacedObject, WorldObjects;
export 'src/features/production/production.dart' show Production;
export 'src/features/quests/quests.dart' show Quests;
export 'src/features/skills/skills.dart' show Skills;
export 'src/features/social/social.dart' show Social, chatRadius;
export 'src/features/talk/talk.dart' show Talk;
export 'src/inbox.dart' show Inbox, PlayerIntent;
export 'src/persistence/keys.dart' show accountKeyOf, saveKeyOf;
export 'src/persistence/storage.dart' show Storage;
export 'src/server.dart' show ScapeServer;
export 'src/wire.dart' show Wire, WireLink;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeServerVersion = '0.15.0';
