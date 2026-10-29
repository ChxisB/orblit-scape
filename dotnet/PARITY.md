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
