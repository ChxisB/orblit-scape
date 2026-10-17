/// Tile coordinates, flags, paths and the rules a server and a client share.
library;

export 'src/combat.dart'
    show
        CombatBonus,
        CombatRules,
        CombatStyle,
        maxRange,
        NpcFight,
        Rating,
        healthInts,
        healthName;
export 'src/magic.dart'
    show
        SpellDamage,
        SpellDef,
        SpellEffect,
        SpellSet,
        SpellStun,
        SpellTeleport,
        maxSpellId;
export 'src/prayer.dart' show CombatStat, PrayerDef, PrayerSet, maxPrayerId;
export 'src/container.dart'
    show
        Accepts,
        Container,
        ContainerId,
        Outcome,
        SlotChange,
        bankTabSize,
        bankTabs,
        containerSize,
        inventorySize;
export 'src/container_codec.dart' show SlotsDelta, encodeSlots, tryDecodeSlots;
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
        BankOpen,
        Buy,
        Cast,
        Chat,
        Choose,
        ClientMessage,
        Continue,
        ConversationChoices,
        ConversationLine,
        Decoded,
        Deposit,
        EditList,
        Friend,
        Hit,
        Intent,
        Interact,
        ListKind,
        Login,
        LoginOk,
        LoginRefusal,
        Notice,
        PrayerView,
        PrivateState,
        Projectile,
        Said,
        Sell,
        ServerMessage,
        ShopStock,
        ShopView,
        SocialLists,
        Trade,
        TradeAccept,
        TradeDecline,
        TradeOffer,
        TogglePrayer,
        TradeView,
        Understood,
        Unreadable,
        Walk,
        Whisper,
        Whispered,
        Withdraw,
        WindowClosed,
        isValidName,
        isValidText,
        maxActivePrayers,
        maxChoices,
        maxDeltaBytes,
        maxListEntries,
        maxNameChars,
        maxPasswordBytes,
        maxTextBytes,
        maxTradeLines,
        minPasswordBytes,
        nameKey;
export 'src/loot.dart' show LootEntry, LootItem, LootSub, LootTable;
export 'src/mover.dart' show MoveSpeed, Mover;
export 'src/player_state.dart' show PlayerState, maxSavedXp, playerStateVersion;
export 'src/nodes.dart' show NodeDef, NodeSet, nodeDepleted, nodeFull;
export 'src/npcs.dart'
    show
        Aggressive,
        Behaviour,
        Idle,
        NpcDef,
        NpcOption,
        NpcSet,
        NpcSpawn,
        Wander,
        maxNpcId,
        npcInts,
        npcName;
export 'src/objects.dart'
    show
        InteractKind,
        ObjectDef,
        ObjectOption,
        ObjectSet,
        maxObjectId,
        worldObjectInts,
        worldObjectName;
export 'src/path.dart'
    show PathResult, defaultPathMargin, findPath, findPathToReach;
export 'src/line_of_sight.dart' show hasClearLine;
export 'src/items.dart'
    show
        ItemDef,
        ItemSet,
        ItemStack,
        Needs,
        equipmentSlots,
        groundItemInts,
        groundItemName,
        itemsDeltaTag,
        maxItemId,
        maxStack;
export 'src/recipes.dart'
    show
        Failure,
        Gives,
        Lights,
        OnItem,
        OnObject,
        Product,
        Recipe,
        RecipeSet,
        RecipeStart,
        maxMakeTicks;
export 'src/shops.dart'
    show ShopDef, ShopLine, ShopRules, maxShopLines, maxShopStock;
export 'src/skills.dart'
    show SkillDef, SkillSet, XpCurve, maxLevel, skillsDeltaTag;
export 'src/reach.dart'
    show Beside, Footprint, OnTile, Reach, Within, reachSatisfied;
export 'src/story.dart'
    show QuestReward, QuestStep, Story, maxRewardItems, questsDeltaTag;
export 'src/tick.dart' show tickPeriod;
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
const scapeCoreVersion = '0.19.0';
