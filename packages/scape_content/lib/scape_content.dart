/// The content package of orblit-scape.
library;

export 'src/combat.dart' show defaultCombat;
export 'src/items.dart' show ItemId, defaultItems;
export 'src/magic.dart' show SpellId, defaultSpells;
export 'src/nodes.dart' show defaultNodes;
export 'src/models.dart'
    show ActorModel, ModelBone, ModelBox, ModelSet, Vec3, defaultModels;
export 'src/npcs.dart' show NpcId, defaultNpcs;
export 'src/objects.dart' show ObjectId, defaultObjects;
export 'src/poses.dart' show Pose, actSeconds, poseOf;
export 'src/prayer.dart' show PrayerId, defaultPrayers;
export 'src/recipes.dart' show defaultRecipes;
export 'src/shops.dart' show defaultShops;
export 'src/skills.dart' show defaultSkills;
export 'src/story.dart' show defaultStory;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeContentVersion = '0.12.0';
