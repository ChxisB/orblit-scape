/// The content package of orblit-scape.
library;

export 'src/items.dart' show ItemId, defaultItems;
export 'src/nodes.dart' show defaultNodes;
export 'src/npcs.dart' show NpcId, defaultNpcs;
export 'src/objects.dart' show ObjectId, defaultObjects;
export 'src/skills.dart' show defaultSkills;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeContentVersion = '0.6.0';
