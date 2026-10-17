import 'items.dart';
import 'objects.dart';
import 'skills.dart';

/// The state of a node object that can be gathered from.
const int nodeFull = 0;

/// The state of a node object that has run dry until it respawns.
const int nodeDepleted = 1;

/// One kind of resource a player gathers from: a tree, a rock, a fishing
/// spot. It is a row of content that names the world object it lives on.
final class NodeDef {
  NodeDef({
    required this.object,
    required this.skill,
    required this.level,
    required this.tool,
    required this.xp,
    required this.product,
    required this.chanceLow,
    required this.chanceHigh,
    required this.fullLevel,
    required this.depleteChance,
    required this.respawnTicks,
  }) {
    if (level < 1 || level > maxLevel) {
      throw ArgumentError.value(level, 'level', 'is outside 1 to $maxLevel');
    }
    if (fullLevel <= level || fullLevel > maxLevel) {
      throw ArgumentError.value(fullLevel, 'fullLevel', 'is not above level');
    }
    if (xp < 1) throw ArgumentError.value(xp, 'xp', 'is below one');
    if (respawnTicks < 1) {
      throw ArgumentError.value(respawnTicks, 'respawnTicks', 'is below one');
    }
    _checkChance(chanceLow, 'chanceLow');
    _checkChance(chanceHigh, 'chanceHigh');
    _checkChance(depleteChance, 'depleteChance');
    if (chanceHigh < chanceLow) {
      throw ArgumentError.value(chanceHigh, 'chanceHigh', 'is below chanceLow');
    }
  }

  /// The id of the [ObjectDef] this node is.
  final int object;

  final String skill;

  /// The skill level needed to gather.
  final int level;

  /// The item the player must hold or wear.
  final int tool;

  /// Experience for each success.
  final int xp;

  /// The item a success gives.
  final int product;

  /// The chance of a success at [level], from 0 to 1.
  final double chanceLow;

  /// The chance of a success at [fullLevel] and above.
  final double chanceHigh;

  final int fullLevel;

  /// The chance, after a success, that the node runs dry.
  final double depleteChance;

  /// Ticks a depleted node stays dry.
  final int respawnTicks;

  /// The chance that one try succeeds for a player of [skillLevel]. It
  /// rises in a straight line from [chanceLow] at [level] to [chanceHigh] at
  /// [fullLevel].
  double chanceAt(int skillLevel) {
    if (skillLevel >= fullLevel) return chanceHigh;
    if (skillLevel <= level) return chanceLow;
    final along = (skillLevel - level) / (fullLevel - level);
    return chanceLow + (chanceHigh - chanceLow) * along;
  }
}

void _checkChance(double chance, String name) {
  if (chance.isNaN || chance < 0 || chance > 1) {
    throw ArgumentError.value(chance, name, 'is outside 0 to 1');
  }
}

/// The nodes a game has. Each must name an object, items and a skill that
/// exist, so a typo fails when the game starts and not when a player tries.
final class NodeSet {
  NodeSet({
    required List<NodeDef> nodes,
    required ObjectSet objects,
    required ItemSet items,
    required SkillSet skills,
  }) : nodes = List.unmodifiable(nodes),
       _byObject = {for (final node in nodes) node.object: node} {
    if (_byObject.length != nodes.length) {
      throw ArgumentError.value(nodes, 'nodes', 'repeats an object');
    }
    for (final node in nodes) {
      objects.require(node.object);
      items
        ..require(node.tool)
        ..require(node.product);
      if (skills.find(node.skill) == null) {
        throw ArgumentError.value(node.skill, 'skill', 'is unknown');
      }
    }
  }

  final List<NodeDef> nodes;
  final Map<int, NodeDef> _byObject;

  /// The node that lives on the object [object], or null.
  NodeDef? find(int object) => _byObject[object];
}
