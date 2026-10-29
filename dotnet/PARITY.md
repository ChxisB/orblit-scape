# Parity

One row per Dart test file that has been ported. A row is added when the file
is built.

Dart declared counts the test declarations in the file. Dart run counts the
tests that execute. C# ported counts the C# tests that carry a Dart test over.
Added in C# counts tests with no Dart twin. A file is done when C# ported
equals Dart run.

The rules are the engine's `dotnet/PORTING.md`, in the `orblit` repository. The
engine packages come from a folder. The engine's `tool/pack_local.sh` packs
them, into `~/.orblit/feed` unless `ORBLIT_FEED` names another.
`Directory.Packages.props` pins the versions.

A fixture holds what the Dart gave for a set of cases, and a parity test
compares the C# with it. `tool/regen_parity.sh` runs the exporters in
`tool/parity` and writes `dotnet/fixtures`. The Dart pubspecs resolve only
after `tool/link_local.sh` points them at local engine and net checkouts.
`tool/check_dotnet.sh` builds, tests and checks the format.

| Dart test file | C# class | Dart declared | Dart run | C# ported | Added in C# | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `scape_core/test/scape_core_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Core.csproj`, so there is no constant to drift from it |
| `scape_core/test/tile_map_test.dart` | `TileMapTests` | 17 | 17 | 17 | 0 | done |
| `scape_core/test/tile_position_test.dart` | `TilePositionTests` | 3 | 3 | 3 | 1: a point that is not a number has no nearest tile | done |
| `scape_core/test/region_file_test.dart` | `RegionFileTests` | 5 | 5 | 5 | 0 | done |
| `scape_core/test/path_test.dart` | `PathTests` | 21 | 21 | 21 | 0 | done. The random maps use `System.Random`, so they differ from the Dart's. The fixture carries the Dart's maps |
| `scape_core/test/objects_test.dart` | `ObjectsTests` | 11 | 11 | 11 | 0 | done |
| none, the fixture `scape_core/world.json` | `WorldParityTests` | 0 | 0 | 0 | 7: 120 path, 30 sight, 2 encode, 10 decode, 3 position, 3 read and 7 nearest cases | added. The path steps pin the order the search tries its neighbours in, and the decode refusals compare the reader's messages |
| `scape_core/test/skills_test.dart` | `SkillsTests` | 9 | 9 | 9 | 0 | done |
| `scape_core/test/combat_test.dart` | `CombatTests` | 16 | 16 | 16 | 2: a style outside the enum beats nothing, and a huge bonus rolls high and is not wrapped | done. A roll is a `long` where the Dart multiplies past an int |
| `scape_core/test/items_test.dart` | `ItemsTests` | 8 | 8 | 8 | 0 | done. An int cannot hold a count above `ItemStack.MaxCount`, so that half of one test has no twin |
| `scape_core/test/actor_test.dart` | `ActorTests` | 12 | 12 | 12 | 0 | done. The test of bad Anim bytes is a theory of 6 rows |
| `scape_core/test/loot_test.dart` | `LootTests` | 15 | 15 | 15 | 0 | done |
| `scape_core/test/shops_test.dart` | `ShopsTests` | 8 | 8 | 8 | 1: the dearest item's price is not wrapped | done. A price is a `long` where the Dart multiplies past an int |
| `scape_core/test/npcs_test.dart` | `NpcsTests` | 9 | 9 | 9 | 0 | done |
| `scape_core/test/nodes_test.dart` | `NodesTests` | 7 | 7 | 7 | 0 | done |
| `scape_core/test/recipes_test.dart` | `RecipesTests` | 17 | 17 | 17 | 0 | done |
| `scape_core/test/story_test.dart` | `StoryTests` | 4 | 4 | 4 | 0 | done |
| `scape_core/test/ranged_magic_prayer_test.dart` | `RangedMagicPrayerTests` | 17 | 17 | 17 | 1: a projectile cannot be built with a style outside the enum | done |
| none, the fixture `scape_core/rules.json` | `RulesParityTests` | 0 | 0 | 0 | 8: 12 curve, 40 hit, 60 swing, 30 loot, 18 anim, 40 animRead, 12 look and 30 lookRead cases | added. The dice travel in the fixture and each draw names its bound, so a port that draws differently fails at that draw |
| `scape_core/test/container_test.dart` | `ContainerTests` | 74 | 74 | 74 | 2: an id outside the enum makes no container, and an add leaves a full stack it skips clean | done. A count is a `long` where the Dart sums past an int |
| none, the fixture `scape_core/containers.json` | `ContainerParityTests` | 0 | 0 | 0 | 2: 30 script cases of 80 steps over the three containers, and 40 restore cases | added. Each step's result is compared, then every slot and dirty mark of the three containers at the end |
| `scape_core/test/messages_test.dart` | `MessagesTests` | 25 | 25 | 25 | 1: one leading byte order mark is dropped from text | done. .NET keeps a byte order mark that Dart's `utf8.decode` drops, so `Wire.DecodeText` drops one |
| `scape_core/test/player_state_test.dart` | `PlayerStateTests` | 24 | 24 | 24 | 2: a container outside the enum is refused, and reading drops a leading byte order mark | done |
| `scape_core/test/conversation_messages_test.dart` | `ConversationMessagesTests` | 6 | 6 | 6 | 0 | done |
| `scape_core/test/move_item_messages_test.dart` | `MoveItemMessagesTests` | 3 | 3 | 3 | 1: a move cannot be built with a container outside the enum | done |
| `scape_core/test/player_trade_messages_test.dart` | `PlayerTradeMessagesTests` | 7 | 7 | 7 | 0 | done |
| `scape_core/test/social_messages_test.dart` | `SocialMessagesTests` | 12 | 12 | 12 | 1: a list edit cannot be built for a list outside the enum | done |
| `scape_core/test/trade_messages_test.dart` | `TradeMessagesTests` | 13 | 13 | 13 | 0 | done |
| none, the fixture `scape_core/wire.json` | `WireParityTests` | 0 | 0 | 0 | 5: 67 built, 458 bytes, 24 encodeSlots, 120 readSlots and 95 state cases | added. Every message is built at its edges and read by both decoders. The bytes cases add damaged, random, odd UTF-8 and loose flag bytes. The state cases cover JSON edges such as duplicate keys, `-0` and a lone surrogate |
| `scape_content/test/scape_content_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Content.csproj`, so there is no constant to drift from it |
| `scape_content/test/skills_test.dart` | `SkillsTests` | 2 | 2 | 2 | 0 | done |
| `scape_content/test/items_test.dart` | `ItemsTests` | 4 | 4 | 4 | 0 | done |
| `scape_content/test/magic_prayer_test.dart` | `MagicPrayerTests` | 3 | 3 | 3 | 0 | done |
| `scape_content/test/objects_test.dart` | `ObjectsTests` | 4 | 4 | 4 | 0 | done |
| `scape_content/test/nodes_test.dart` | `NodesTests` | 3 | 3 | 3 | 0 | done |
| `scape_content/test/recipes_test.dart` | `RecipesTests` | 4 | 4 | 4 | 0 | done |
| `scape_content/test/npcs_test.dart` | `NpcsTests` | 5 | 5 | 5 | 0 | done |
| `scape_content/test/story_test.dart` | `StoryTests` | 3 | 3 | 3 | 0 | done |
| none, the fixture `scape_content/data.json` | `DataParityTests` | 0 | 0 | 0 | 9: 1 sets, 22 item, 10 object, 3 node, 5 recipe, 5 npc, 3 spell, 3 prayer and 1 story cases | added. Every definition is compared field by field. The quests and dialogues are compared as JSON trees |
| `scape_content/test/poses_test.dart` | `PosesTests` | 9 | 9 | 9 | 0 | done |
| `scape_content/test/models_test.dart` | `ModelsTests` | 12 | 12 | 12 | 0 | done |
| none, the fixture `scape_content/look.json` | `LookParityTests` | 0 | 0 | 0 | 5: 8 act, 372 pose, 1 npcIds, 7 npc and 89 player cases | added. Each state is posed on a time grid from -0.1 to 2 seconds, at the points its tests look at and at 1000.37 seconds. The players cover each slot with six items, the colour edges and 20 random looks |
| `scape_content/test/check_test.dart` | `CheckTests` | 28 | 28 | 28 | 1 | done. The added test needs three makes from the shop, so `Obtainable` must loop |
| none, the fixture `scape_content/island.json` | `IslandParityTests` | 0 | 0 | 0 | 3: 18 region, 1 plan and 104 problems cases | added. Each region key from (0, 0, 0) to (2, 2, 1) gives its flag bytes or null. The problems cases break the island at random: a new spawn tile, flags, dropped and added placements and spawns, and dropped shop lines, recipes and dialogues. Three more are written out: the spawn tile on a solid object, stairs that land on one and on blocked dry ground, and logs and the axe gone from the shop |
| none, the fixture `scape_server/saves.json` | none yet | 0 | 0 | 0 | 9: 7 key, 49 trade, 22 journalWrite, 20 journalRead, 8 hash, 8 create, 19 verify, 27 ledgerWrite and 29 ledgerRead cases | exported. Row 3 adds the C# test. A Dart save, account or ledger must load in the C# server. The cases cover a version of `1.0`, an `until` that must be a whole number up to 8640000000000000, base64 without padding, a byte order mark and repeated keys. The 8 hash cases match libsodium's Argon2id |
| `scape_server/test/action_test.dart` | `ActionTests` | 14 | 14 | 14 | 0 | done |
| `scape_server/test/registry_test.dart` | `RegistryTests` | 3 | 3 | 3 | 0 | done |
| `scape_server/test/tick_loop_test.dart` | `TickLoopTests` | 6 | 6 | 6 | 0 | done. The phase order test also records that due actions run in the actions phase, because a mutant that moved them to the creature phase passed |
| `scape_server/test/inbox_test.dart` | `InboxTests` | 5 | 5 | 5 | 0 | done |
| `scape_server/test/health_test.dart` | `HealthTests` | 3 | 3 | 3 | 1 | done. The added test pins the late tick line to list the slowest phase first, because a mutant that sorted it quickest first passed |
| `scape_server/test/wire_test.dart` | `WireTests` | 12 | 12 | 12 | 0 | done |
| `scape_server/test/interest_test.dart` | `InterestTests` | 6 | 6 | 6 | 0 | done |
| `scape_server/test/movement_test.dart` | `MovementTests` | 11 | 11 | 11 | 0 | done |
| `scape_server/test/social_test.dart` | `SocialTests` | 18 | 18 | 18 | 0 | done. The Dart test for a name already on a list never ran a tick, so the port ticks |
| `scape_server/test/items_test.dart` | `ItemsTests` | 31 | 31 | 31 | 0 | done |
| `scape_server/test/npcs_test.dart` | `NpcsTests` | 24 | 24 | 24 | 0 | done |
| `scape_server/test/actors_test.dart` | `ActorsTests` | 17 | 17 | 17 | 0 | done. The miss test watches ten ticks, because in the Dart the rat's swing back hid a hurt |
| `scape_server/test/objects_test.dart` | `ObjectsTests` | 8 | 8 | 8 | 0 | done |
| `scape_server/test/interactions_test.dart` | `InteractionsTests` | 27 | 27 | 27 | 0 | done |
| `scape_server/test/skills_test.dart` | `SkillsTests` | 15 | 15 | 15 | 2: a grant near the long limit stops at the cap, and a player who is gone holds no experience | done |
| `scape_server/test/gathering_test.dart` | `GatheringTests` | 20 | 21 | 21 | 0 | done. One Dart test runs for rock and fishing spot, so the port is a theory with two rows |
| `scape_server/test/production_test.dart` | `ProductionTests` | 24 | 24 | 24 | 0 | done |
| `scape_server/test/bank_test.dart` | `BankTests` | 9 | 9 | 9 | 1: nothing moves while a shop is open | done |
| `scape_server/test/shops_test.dart` | `ShopsTests` | 21 | 21 | 21 | 7: the bank window does not trade, exact coins buy, another shop's viewers hear nothing, an unchanged shop sends no view, restock lands one interval after the last, and coins or a line dearer than a stack of coins fail at startup | done |
| `scape_server/test/combat_test.dart` | `CombatTests` | 33 | 33 | 33 | 1: a slow weapon swings at its own speed | done |
| `scape_server/test/loot_test.dart` | `LootTests` | 6 | 6 | 6 | 0 | done |
| `scape_server/test/ranged_magic_prayer_test.dart` | `RangedMagicPrayerTests` | 20 | 26 | 26 | 1: a spell is ready again on the tick its speed runs out | done. The Dart loops over four shot distances and five style matchups, so the port has two theories for them |
| `scape_server/test/talk_test.dart` | `TalkTests` | 20 | 20 | 18 | 11: a saved stage at the last is kept, an absent player's restore leaves out every stage, a restore that changes nothing sends nothing, the table goes when the player leaves, a dialogue no variant starts has nothing to say, the window closes at the end, a conversation goes no further once its window closes, a choice of exactly the client's limit starts, and a quest title, a reward skill or a reward item the game lacks fails at startup | 2 move to 3f: a quest advances across a restart, and a server with no story refuses the save and leaves it |
| `scape_server/test/trades_test.dart` | `TradesTests` | 29 | 33 | 18 | 11: a line is taken back when the offer is full, a gift with nothing back goes through, a trade stays open when a player no longer holds the offer, a request from a player in a trade or to a player with another window is refused, a request is asked again once a trade ends, a request goes when its maker leaves, a click on oneself asks nothing, a trade opens two tiles off and not three, and a trade ended by opening the bank leaves the bank open | 15 move to 3f: the journal check of one test, 6 crash tests from 2 declarations, and 8 journal tests. The trade window's ref is 0, where the Dart sends the partner's network id. Settled waits for 3f |
| `scape_server/test/island_test.dart` | `IslandTests` | 5 | 5 | 5 | 0 | done. The stock test also checks each object's destination |
| `scape_server/test/economy_test.dart` | `EconomyTests` | 1 | 1 | 1 | 0 | done |
