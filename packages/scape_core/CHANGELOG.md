# Changelog

## 0.21.0

- `ActorState`, `Anim` and `Look`, with the bytes of the `Anim` and `Look`
  components. `Anim` holds how an actor rests, the act it last played and a
  count of acts. `Look` holds three colours and the item worn in each
  equipment slot. The acts are attack, hurt, death, gather and make.

## 0.20.0

- `MoveItem` drags a stack from one slot to another, between the inventory
  and the equipment. The bank is not moved this way. `Container.swapWith`
  swaps two slots across containers and says why it refused.

## 0.19.0

- Ranged combat, magic and prayer. `CombatStyle` is melee, ranged or magic,
  and each beats one other for a 1.25 edge on a swing. `CombatBonus` takes a
  `style`, a `range` and an `ammo` kind, and an item may sit in the new
  `ammo` equipment slot. A ranged weapon needs a speed, a range and an
  ammo kind. `NpcFight.style` gives a creature a style.
  `CombatRules.flightTicks` is how long a shot flies.
- `SpellSet` and `SpellDef` describe spells that damage, stun or teleport.
  `PrayerSet` and `PrayerDef` describe prayers that raise a combat stat and
  drain points.
- `Cast` and `TogglePrayer` are intents. `Projectile` and `PrayerView` are
  what the server sends back.

## 0.18.0

- Trading between players. `InteractKind.player` is a click on another
  player. `TradeOffer` sets how many of an item a player puts up, and zero
  takes it back. `TradeAccept` and `TradeDecline` answer. `TradeView` is
  what each side sees: both offers, the step, and who has accepted.
- `Container.tryExchange` answers what an `exchange` would answer and
  changes nothing.

## 0.17.0

- `Story` holds a game's quests, dialogues, named steps and rewards, and
  builds the one progress table every player's quests live in. A step or
  reward that names a quest or a stage the game lacks throws at start.
- `Continue` and `Choose` are the two things a player does in a
  conversation. `ConversationLine` and `ConversationChoices` are what
  the server shows. A choice holds at most eight options.
- `NpcOption.talk` and `NpcDef.dialogue`. A creature with a dialogue can
  be talked to.
- `PlayerState.quests` keeps each quest's stage by quest key. A save from
  before reads as having begun none. `playerStateVersion` is 2, so an
  older server refuses a file that holds quests and cannot erase them.
- `questsDeltaTag` marks a quest change in a `PrivateState`.

## 0.16.0

- `PlayerState` is what a server keeps of one player between logins: the
  tile, experience by skill id, the slots of each container and the
  friends and ignore lists. `encode` writes sorted JSON, so the same
  state gives the same bytes. `decode` throws a `FormatException` for
  anything damaged and for a version from a later server.
- `Container.slots` copies the stacks out. `Container.restore` puts them
  back into an empty container and returns how many it had to leave out,
  because the game no longer has the item, the slot no longer takes it, or
  a non-stackable item holds more than one.
- `containerSize` gives the number of slots of a container id.
- `Login` carries a password and whether to create the account. The
  login layout changed, so a 0.15.0 client cannot log in to a 0.16.0
  server. `LoginRefusal` adds `badLogin`, `weakPassword`,
  `accountExists`, `busy` and `unavailable`.

## 0.15.0

- `Recipe` says what a player makes: the inputs it uses, the tool it
  needs in hand, the skill and level it asks for, the experience it
  pays and what it gives. A make can fail with a chance that falls to
  zero at a safe level. `Lights` puts a fire down for a number of ticks.
- `RecipeSet` checks every recipe names items, an object and a skill
  that exist, and that no two recipes share a start.

## 0.14.0

- `LootTable` pays out on a roll: fixed drops, a weighted pick that may
  be nothing or another table, and a rare tier that adds a drop once in
  so many rolls. The caller passes the dice. `NpcDef.loot` holds one.
- `ShopRules` prices things from an item's value. `ShopDef` and
  `ShopLine` say what a shopkeeper stocks, how much, and how fast it
  restocks. `NpcDef.shop` holds one.
- `NpcOption` names the options of a creature, attack and trade.
- `Container.exchange` takes and gives as one change that either all
  happens or leaves the container as it was. `occupied` and `search` give
  the tab view and the name search of a bank.
- New messages: `Buy` and `Sell` for a shop, `Deposit` and `Withdraw`
  for the bank, and `ShopView`, `BankOpen` and `WindowClosed` back.

## 0.13.0

- `CombatRules` holds the numbers a fight is worked out from: the hit
  chance, the most a hit does, health, swing speed and experience.
  `CombatBonus` is what a worn item adds. `NpcFight` is how a creature
  fights, and `NpcDef.fight` is null for one that cannot be attacked.
- `ItemDef.combat` gives a worn item its bonus. A weapon names its swing
  speed.
- `Hit` is a server message with the target and the damage, 0 for a
  miss. `InteractKind.npc` attacks a creature.
- `healthName` names the replicated hit points.

## 0.12.0

- `NpcDef`, `NpcSet` and `NpcSpawn` say what a creature is and where it
  stands. A creature is idle, wanders, or is aggressive with a sight and
  a leash.
- `npcName` names the replicated component that carries a creature's id.

## 0.11.0

- `NodeDef` and `NodeSet` say what a tree, a rock or a fishing spot gives:
  the skill, level, tool, experience, product, the chance of a success
  from level to full level, and how long a dry node stays dry.
- `nodeFull` and `nodeDepleted` are the two states of a node object.
- `Container.canHold` says whether one more of an item would go in.

## 0.10.0

- `ObjectDef` and `ObjectSet` say what an object is: its size, whether it
  blocks while closed, and the options a player may pick. An option names
  an action and may be offered only in one state.
- `InteractKind` names the four things a click can mean. The `Interact`
  intent carries one.
- `TileFlags.without` clears a flag and leaves the others.

## 0.9.0

- `ItemDef` and `ItemSet` say what an item is. `ItemStack` is a count of
  one item, up to `maxStack`, which fits a signed 32-bit integer.
- `Container` is a row of slots with `add`, `take`, `move`, `swap`,
  `stack`, `split` and `transfer`. Each call moves everything it reports
  or nothing, and a refusal is an `Outcome`. `Container.forId` builds the
  inventory, the equipment and the bank.
- `encodeSlots` and `tryDecodeSlots` write and read the slots that changed
  as `PrivateState` deltas. `itemsDeltaTag` starts one. A bank is sent in
  as many deltas as it needs.
- `groundItemName` and `groundItemInts` name the replicated component of
  an item lying on a tile.

## 0.8.0

- `SkillDef`, `XpCurve` and `SkillSet` say what a skill is and what level
  an amount of experience gives. The curve uses whole numbers, so a server
  and a client agree on every platform.
- `skillsDeltaTag` starts a `PrivateState` delta that carries skill changes.
- Depends on `orblit_progress`.

## 0.7.0

- `Whisper`, `EditList` and `ListKind` are client messages. `Said`,
  `Whispered` and `SocialLists` are server messages. Each decoder returns
  `Unreadable` for a bad body and never throws.
- `maxListEntries` caps a friend or ignore list at 50 names.

## 0.6.0

- `TilePosition` is named once: its component name, its layout and the
  functions that write and read it, so a client and a server cannot disagree
  about which float is which.
- `tickPeriod` moved here from `scape_server`, since a client blends over it.

## 0.5.0

- `Avatar` tells a client which replicated entity is its own, by network id.

## 0.4.0

- Messages a client and a server send each other, named once: `Login`,
  `Walk`, `Interact` and `Chat` one way, `LoginOk`, `PrivateState` and
  `Notice` the other. A login refusal is a `LoginRefusal`, sent as the text
  of a goodbye.
- Each message has a size cap. `decodeClientMessage` and
  `decodeServerMessage` return `Understood` or `Unreadable` with a reason and
  never throw. A test feeds them 20,000 random messages.

## 0.3.0

- `findPath` and `findPathToReach`: the shortest walk over the flags, a
  diagonal counting as one step, inside a box round the start and the
  target. A target that cannot be reached gives the nearest reachable tile
  and says so.
- `Mover` holds a queue of steps. It walks a tile a tick and runs two, and a
  step the map no longer allows ends the walk.
- Reach rules: `OnTile`, `Beside` a footprint and `Within` a range of one,
  with `hasClearLine` for the range.
- The enum for who is stepping is `Walker`.

## 0.2.0

- Tiles: `TileCoord`, `RegionKey` and `TileFlags`. A region is 64 by 64
  tiles of one plane, and the world is 256 regions across.
- `TileMap.canStep` answers whether one step in eight directions is
  allowed. A diagonal needs both corner tiles open and no wall round them.
- `FlagRegion` and its file: a magic, a version, the key, then one byte a
  tile. A reader refuses a version it does not know.

## 0.1.0

- The package exists. It holds a version constant and a test that keeps it
  equal to the version in `pubspec.yaml`.
