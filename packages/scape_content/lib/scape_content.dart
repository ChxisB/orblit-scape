/// The content package of orblit-scape.
library;

export 'src/combat.dart' show defaultCombat;
export 'src/items.dart' show ItemId, defaultItems;
export 'src/nodes.dart' show defaultNodes;
export 'src/npcs.dart' show NpcId, defaultNpcs;
export 'src/objects.dart' show ObjectId, defaultObjects;
export 'src/recipes.dart' show defaultRecipes;
export 'src/shops.dart' show defaultShops;
export 'src/skills.dart' show defaultSkills;
export 'src/story.dart' show defaultStory;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeContentVersion = '0.10.0';
