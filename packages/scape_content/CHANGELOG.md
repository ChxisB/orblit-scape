# Changelog

## 0.11.0

- Magic and prayer are skills, so there are eleven.
- A shortbow, bronze arrows and runes are items. The shopkeeper sells
  them.
- `defaultSpells` has Strike, Daze and Home teleport. `defaultPrayers`
  has Thick skin, Burst of strength and Clarity of thought. Each raises its
  stat by 10 percent.

## 0.10.0

- `defaultStory` has two quests, The Dark Lantern and Goblin Count, and
  three conversations. The second quest opens when the first is done.
  Each quest gives experience and coins.
- Brindle the lamplighter is a creature. The Guard and the Shopkeeper
  have something to say.

## 0.9.0

- Four recipes: cook the fish, light the logs, carve the logs and forge
  the sword. A fire and an anvil are objects.
- Burnt fish, a knife, a tinderbox, arrow shafts and a hammer are items.
  The shopkeeper sells the tools.
- Firemaking and fletching are skills, so there are nine.

## 0.8.0

- A bank booth object opens the player's bank.
- Bones and feathers. The chicken and the goblin drop them, with coins
  and sometimes gear. The goblin has a rare sword.
- A shopkeeper creature sells axes, pickaxes, nets, logs and copper
  ore. `defaultShops` prices things in coins.

## 0.7.0

- `defaultCombat` holds the rules of the first game.
- The chicken and the goblin can be fought. The guard cannot.
- The sword, axe, pickaxe, shield and helmet add to a fight.

## 0.6.0

- `defaultNpcs` gives the chicken, goblin and guard their behaviour.
  `NpcId` names each one.

## 0.5.0

- `defaultNodes` gives the tree, rock and fishing spot their numbers.
- Objects: tree, rock and fishing spot. Item: fishing net.

## 0.4.0

- `defaultObjects`: a door that opens and closes, and stairs up and
  down. `ObjectId` names each one.

## 0.3.0

- `defaultItems`: coins, logs, copper ore, bronze bars, raw and cooked
  fish, a bronze axe, pickaxe, sword and helmet, and a wooden shield.
  `ItemId` names each one.

## 0.2.0

- `defaultSkills`: woodcutting, mining, fishing, cooking, smithing,
  fighting and health, each capped at level 50.

## 0.1.0

- The package exists. It holds a version constant and a test that keeps it
  equal to the version in `pubspec.yaml`.
