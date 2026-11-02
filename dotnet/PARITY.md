# Parity

The Dart is no longer in this repository. The last commit that has it is
tagged `dart-final`, and the Dart test counts below were taken at that tag.
The server has no host program and no Argon2id hasher yet, so the 8 hash
cases of the saves fixture have no test.

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
compares the C# with it. The fixtures in `dotnet/fixtures` are frozen. The
Dart exporters that wrote them went with the Dart, so `tool/parity` is at the
tag `dart-final`. `tool/check_dotnet.sh` builds, tests and checks the format.

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
| none, the fixture `scape_server/saves.json` | `SavesParityTests` | 0 | 0 | 0 | 8: 7 key, 49 trade, 22 journalWrite, 20 journalRead, 27 ledgerWrite, 29 ledgerRead, 8 create and 19 verify cases | added for those. The 8 hash cases wait on decision 100. The create and verify cases use the exporter's stand-in hasher, which gives the salt and then the password. A Dart save, account or ledger must load in the C# server. The cases cover a version of `1.0`, an `until` that must be a whole number up to 8640000000000000, base64 without padding, a byte order mark and repeated keys. The 8 hash cases match libsodium's Argon2id |
| `scape_server/test/scape_server_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Server.csproj`, so there is no constant to drift from it |
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
| `scape_server/test/talk_test.dart` | `TalkTests` | 20 | 20 | 20 | 11: a saved stage at the last is kept, an absent player's restore leaves out every stage, a restore that changes nothing sends nothing, the table goes when the player leaves, a dialogue no variant starts has nothing to say, the window closes at the end, a conversation goes no further once its window closes, a choice of exactly the client's limit starts, and a quest title, a reward skill or a reward item the game lacks fails at startup | done |
| `scape_server/test/trades_test.dart` | `TradesTests` | 29 | 33 | 33 | 11: a line is taken back when the offer is full, a gift with nothing back goes through, a trade stays open when a player no longer holds the offer, a request from a player in a trade or to a player with another window is refused, a request is asked again once a trade ends, a request goes when its maker leaves, a click on oneself asks nothing, a trade opens two tiles off and not three, and a trade ended by opening the bank leaves the bank open | done. The trade window's ref is 0, where the Dart sends the partner's network id. The Dart loops over 0 to 4 writes kept in a crash, so the port is a theory with five rows |
| `scape_server/test/island_test.dart` | `WorldTests` | 5 | 5 | 5 | 0 | done. The stock test also checks each object's destination. They stand on the small `TestWorld` in `Scape.Content.Tests`, since the starter island is gone |
| `scape_server/test/economy_test.dart` | `EconomyTests` | 1 | 1 | 1 | 0 | done |
| `scape_server/test/persistence_test.dart` | `PersistenceTests` | 10 | 10 | 10 | 7: a login that found its session held joins with what the world had when the session ends first, a restart gives the health the saved skill allows, a restart logs what a save has that the game no longer has, a login after a failed trade recovery tries it again, a second login for a name still being decided hears busy, a removed player is saved as a leave and a dropped one as a disconnect, and a stop logs the saves it could not write | done. The C# host decides a login off the tick, so a held session can end before the host seats the player. The first added test plays the host's steps by hand in its order, because no hook reaches that window. A stop disposes the host before it saves, where the Dart saves first, so no join or leave changes the world while it is saved. Dropping the sweep that forgets held logins survives mutants. A held login it misses only keeps that player's state in memory until their next login, which replaces it |
| `scape_server/test/accounts_test.dart` | `AccountsTests` | 11 | 11 | 10 | 0 | 1 waits on decision 100: Argon2id gives the same hash for the same salt and no other. The other tests use a fast stand-in hasher |
| `scape_server/test/admin_test.dart` | `AdminTests` | 31 | 31 | 31 | 10 | done. A kick removes the player off the tick, so the tests wait for its goodbye before they look. The bad input test also checks that each line adds one notice, so a line that says nothing cannot pass on the answer before it. A Dart command awaits its work, so its answer lands a microtask after the tick. The C# sends it at once. A command's words are split on the spaces JavaScript's `\s` takes, listed, because .NET's `\s` also takes U+0085 and leaves out U+FEFF. A command that crashes logs the error's message, where the Dart logs its `toString`. The ledger refuses a change before it has loaded, where the Dart changes the record in memory and then throws. A rollback counts in ticks, since enough minutes reach back past the year 1. Nine added tests come from mutation runs. They cover the edges of a mute, a shutdown and a notice, a player who has left, the rollback's checks, the entrance's screens and a ledger that is not text. The tenth comes from the review. A shutdown of the longest time the wire holds overflowed an int and stopped at once. A one-second shutdown says 2 seconds after 1 second, as the Dart does. That is decision 80 item 105 |
| `scape_server/test/config_test.dart` | `ConfigTests` | 6 | 23 | 23 | 2 | done. The Dart loops over 18 bad values, so the port is a theory with 18 rows. A port in hex such as `0x1F40` stops startup, where Dart's `int.tryParse` takes it. .NET's `Trim` keeps U+FEFF, which Dart's `trim` removes, so an admin name that holds one stops startup. The two added cases come from mutation runs: a world name that ends in a newline, and `info` written out |
| `scape_client/test/orbit_view_test.dart` | `OrbitViewTests` | 6 | 6 | 6 | 1: every part of the target must be finite | done |
| `scape_client/test/tile_pick_test.dart` | `TilePickTests` | 5 | 5 | 5 | 3: a point near a tile's edge picks that tile, ground past the far edge of the world picks nothing, and an actor off the ground stands at height zero | done |
| `scape_client/test/animator_test.dart` | `ActorAnimatorTests` | 14 | 14 | 14 | 2: an act that is over stays over after first sight, and an actor that left view dead is not still fallen when it returns | done |
| none, the pose of an actor | `ModelPoseTests` | 0 | 0 | 0 | 8: a still pose leaves every box where the model put it, a limb swung a quarter turn points forward from its pivot for 4 limbs, leaning tips the upper body and leaves the legs, a raised arm on a body bent flat points at the ground, and an actor fallen flat has its head behind its feet | added. The Dart never applies a pose to the boxes. 19 mutants were tried and all were killed |
| `scape_client/test/session_test.dart` | `SessionTests` | 5 | 5 | 5 | 0 | done. A message queues on the link thread and the session applies it in `Advance`, so the harness pump advances every session. Mutants: 60 of 62 killed on `GameSession`, `Menus` and `Minimap`. One left is equivalent, since the server refuses a trade while any window is open, so a `TradeView` never lands on another window. The other is the `Retain` call that drops the animator's tracks, a leak guard that the session shows nothing of. `ActorAnimatorTests` pins `Retain` itself |
| `scape_client/test/actors_session_test.dart` | `ActorsSessionTests` | 4 | 4 | 4 | 0 | done |
| `scape_client/test/combat_session_test.dart` | `CombatSessionTests` | 3 | 3 | 3 | 1: a wounded fighter shows what is left of their hit points | done |
| `scape_client/test/conversation_session_test.dart` | `ConversationSessionTests` | 2 | 2 | 2 | 0 | done |
| `scape_client/test/npcs_session_test.dart` | `NpcsSessionTests` | 1 | 1 | 1 | 0 | done |
| `scape_client/test/items_session_test.dart` | `ItemsSessionTests` | 9 | 9 | 9 | 0 | done |
| `scape_client/test/objects_session_test.dart` | `ObjectsSessionTests` | 5 | 5 | 5 | 0 | done |
| `scape_client/test/skills_session_test.dart` | `SkillsSessionTests` | 5 | 5 | 5 | 0 | done |
| `scape_client/test/interface_session_test.dart` | `InterfaceSessionTests` | 11 | 11 | 11 | 8: the minimap keeps a tile exactly on the radius and drops one past it, another player is a player dot, an attack entry hits the creature, a trade entry opens the shop and a talk entry starts a conversation, an object entry names its option by its place in the list, a player entry asks for a trade, a finished quest is done in the journal, and the game tab shows the notices | done |
| `scape_client/test/social_session_test.dart` | `SocialSessionTests` | 9 | 9 | 9 | 0 | done |
| `scape_client/test/trade_session_test.dart` | `TradeSessionTests` | 6 | 6 | 6 | 2: starting a conversation closes the bank on the client, and opening the bank closes a conversation | done |
| `scape_client/test/player_trade_session_test.dart` | `PlayerTradeSessionTests` | 3 | 3 | 3 | 0 | done |
| `scape_client/test/ranged_session_test.dart` | `RangedSessionTests` | 2 | 2 | 2 | 0 | done |
| `scape_client/test/socket_game_test.dart` | `SocketGameTests` | 2 | 2 | 2 | 0 | done. The two clients meet over a real WebSocket on this machine, opened by `InProcessGame.ConnectSocketAsync`, since `ScapeServer` has no listen of its own. A wait polls and gives up after 5 s, so no assertion hangs on a fixed sleep. Mutants: 8 of 8 killed on the tests and the harness |
| `scape_client/test/scape_client_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Client.csproj`, so there is no constant to drift from it |
| `scape_bots/test/bots_test.dart` | `BotsTests` | 9 | 9 | 9 | 7: a different seed plays out differently, a bot with no reach stays home, the report names its seed, a report keeps the ticks it was taken at, bots walk to both sides of home and no further than their reach, a bot does nothing until the server lets it in, and bots use the first option of an object | done. A seed fixes a run within C# only, since the random generators differ from Dart's. Links deliver on their own threads, so each bot's link is awaited after it acts, and the same seed played out the same way in 20 of 20 repeat runs |
| `scape_bots/test/cli_test.dart` | `CliTests` | 4 | 10 | 10 | 16: eight more bad arguments, among them a flag given as a value, the lowest and highest seed and reach, a folder that cannot be written to, a path that is not a file name, and the summary of a healthy and of an unhealthy run | done. The Dart loops over 7 bad argument sets, so the port is a theory with 7 rows |
| `scape_bots/test/probe_test.dart` | `ProbeTests` | 9 | 9 | 9 | 2: an area at the far edge of the world, and an object that covers several tiles listed once | done |
| `scape_bots/test/report_test.dart` | `ReportTests` | 10 | 10 | 10 | 3: the left out list and the verdict in the JSON, a tick time in milliseconds, and a p95 exactly at the period is healthy | done. The runtime key in the JSON is `runtime`, not `dart`, and a whole number of milliseconds writes as `10`, not `10.0` |
| `scape_bots/test/scape_bots_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Bots.csproj`, so there is no constant to drift from it |
| `scape_panels/test/panels_test.dart` | `PanelStateTests`, `PanelControllerTests` and `ShellTests` | 17 | 18 | 18 | 48: the ends of the volume and the view distance, a worn stack is picked and not deposited or offered while a bank or a trade is open, a trade offers one more of each kind, a long bank keeps its scroll offset and withdraws the line tapped, a press then a redraw then a release acts once, an item the shell has no name for reads as its number, the 768 edge of the layout, a shell arranged smaller than it was measured, a long chat line wraps, the side panel measures at its own width, a level the server has raised, a finished quest, a second choice, a second menu entry, a creature at the edge of the map keeps its dot inside it, a stack named in the middle of its cell and a detached shell does not draw | done. One Dart test loops over two widths, so 17 declared run 18. The golden compared a picture, so the port checks geometry: the side panel's width, place and tabs at 360 and 1280 wide. A string `handle` call became a typed `PanelController` method. Mutants: 80 of 81 caught on the controller, the state, the panels and the layout. The one left does not build, since it drops the only call to `OpenWindow` |
| `scape_panels/test/scape_panels_test.dart` | none | 1 | 1 | 0 | 0 | not ported. The C# keeps the version once, as `<Version>` in `Scape.Panels.csproj`, so there is no constant to drift from it |
