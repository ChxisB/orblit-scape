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
| `scape_core/test/combat_test.dart` | `CombatTests` | 16 | 16 | 13 | 1: a style outside the enum beats nothing | partial. The Hit group's 3 tests read messages, so they move to 1d with the wire |
| `scape_core/test/items_test.dart` | `ItemsTests` | 8 | 8 | 8 | 0 | done. An int cannot hold a count above `ItemStack.MaxCount`, so that half of one test has no twin |
| `scape_core/test/actor_test.dart` | `ActorTests` | 12 | 12 | 12 | 0 | done. The test of bad Anim bytes is a theory of 6 rows |
| `scape_core/test/loot_test.dart` | `LootTests` | 15 | 15 | 15 | 0 | done |
| `scape_core/test/shops_test.dart` | `ShopsTests` | 8 | 8 | 8 | 0 | done |
| `scape_core/test/npcs_test.dart` | `NpcsTests` | 9 | 9 | 9 | 0 | done |
| `scape_core/test/nodes_test.dart` | `NodesTests` | 7 | 7 | 7 | 0 | done |
| `scape_core/test/recipes_test.dart` | `RecipesTests` | 17 | 17 | 17 | 0 | done |
| `scape_core/test/story_test.dart` | `StoryTests` | 4 | 4 | 4 | 0 | done |
| `scape_core/test/ranged_magic_prayer_test.dart` | `RangedMagicPrayerTests` | 17 | 17 | 12 | 0 | partial. The save test and the messages group, 5 tests, move to 1d with the wire |
| none, the fixture `scape_core/rules.json` | `RulesParityTests` | 0 | 0 | 0 | 8: 12 curve, 40 hit, 60 swing, 30 loot, 18 anim, 40 animRead, 12 look and 30 lookRead cases | added. The dice travel in the fixture and each draw names its bound, so a port that draws differently fails at that draw |
