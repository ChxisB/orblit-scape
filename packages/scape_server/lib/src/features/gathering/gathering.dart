import 'dart:math';

import 'package:scape_core/scape_core.dart';

import '../../engine/act.dart';
import '../../engine/action.dart';
import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../interactions/interactions.dart';
import '../items/items.dart';
import '../objects/objects.dart';
import '../skills/skills.dart';

/// Ticks between one try and the next.
const int gatherTicks = 4;

/// Chopping, mining and fishing. A player who picks a node's option keeps
/// trying every [gatherTicks] until the bag is full, the node runs dry or
/// they do something else. A dry node is bare for the ticks its definition
/// names and then fills again.
final class Gathering {
  Gathering._(
    this._registry,
    this._services, {
    required NodeSet nodes,
    required ItemSet items,
    required WorldObjects objects,
    required Skills skills,
    required Items bags,
    required int? Function(String) actorOf,
    required Random random,
    required Act act,
  }) : _nodes = nodes,
       _items = items,
       _objects = objects,
       _skills = skills,
       _bags = bags,
       _actorOf = actorOf,
       _random = random,
       _act = act;

  /// Adds gathering to [registry]. Gathering is the `gather` option of an
  /// object, so [interactions] decides when it starts.
  static void register(
    Registry registry,
    Services services, {
    required Interactions interactions,
    required NodeSet nodes,
    required ItemSet items,
    required WorldObjects objects,
    required Skills skills,
    required Items bags,
    required int? Function(String) actorOf,
    required Random random,
    required Act act,
  }) {
    final feature = Gathering._(
      registry,
      services,
      nodes: nodes,
      items: items,
      objects: objects,
      skills: skills,
      bags: bags,
      actorOf: actorOf,
      random: random,
      act: act,
    );
    interactions.addObjectAction('gather', feature._begin);
    registry
      ..addKind(feature._gather)
      ..addSystem(Phase.npcs, feature._refill);
  }

  final Registry _registry;
  final Services _services;
  final NodeSet _nodes;
  final ItemSet _items;
  final WorldObjects _objects;
  final Skills _skills;
  final Items _bags;
  final int? Function(String) _actorOf;
  final Random _random;
  final Act _act;
  final ActionKind _gather = ActionKind('gather', delay: gatherTicks);

  /// The tick each dry node fills again. It is a tick, not a count down, so
  /// a node in a cell nobody is near fills on the first tick someone is.
  final Map<PlacedObject, int> _fillsAt = {};

  void _notice(String player, String text) =>
      _services.hosting.tell(player, Notice(text));

  void _begin(String player, PlacedObject object) {
    final node = _nodes.find(object.def.id);
    if (node == null) return;
    final refusal = _refusal(player, node, object);
    if (refusal != null) {
      _notice(player, refusal);
      return;
    }
    _queue(player, node, object);
  }

  void _queue(String player, NodeDef node, PlacedObject object) {
    final actor = _actorOf(player);
    if (actor == null) return;
    _registry.actions.submit(actor, _gather, () => _try(player, node, object));
  }

  void _try(String player, NodeDef node, PlacedObject object) {
    final refusal = _refusal(player, node, object);
    if (refusal != null) {
      _notice(player, refusal);
      return;
    }
    _act(
      _actorOf(player)!,
      ActorState.gather,
    ); // the action was queued for them
    final level = _skills.levelOf(player, node.skill);
    if (_random.nextDouble() < node.chanceAt(level)) {
      _succeed(player, node, object);
    }
    if (object.state == nodeFull) _queue(player, node, object);
  }

  void _succeed(String player, NodeDef node, PlacedObject object) {
    _bags.container(player, ContainerId.inventory)!.add(node.product, 1);
    _skills.award(player, node.skill, node.xp);
    if (_random.nextDouble() < node.depleteChance) {
      _objects.setState(object, nodeDepleted);
      _fillsAt[object] = _registry.actions.tick + node.respawnTicks;
    }
  }

  /// Why [player] cannot gather from [object] now, or null when they can.
  String? _refusal(String player, NodeDef node, PlacedObject object) {
    final inventory = _bags.container(player, ContainerId.inventory);
    if (object.state != nodeFull) return 'There is nothing left.';
    if (_skills.levelOf(player, node.skill) < node.level) {
      return 'You need level ${node.level} in ${node.skill} to do that.';
    }
    if (!_holds(player, node.tool)) {
      final tool = _items.require(node.tool).name.toLowerCase();
      return 'You need a $tool to do that.';
    }
    if (inventory == null || !inventory.canHold(node.product)) {
      return 'Your inventory is full.';
    }
    return null;
  }

  bool _holds(String player, int tool) => [
    ContainerId.inventory,
    ContainerId.equipment,
  ].any((id) => (_bags.container(player, id)?.countOf(tool) ?? 0) > 0);

  void _refill() {
    final tick = _registry.actions.tick;
    for (final MapEntry(key: object, value: due) in _fillsAt.entries.toList()) {
      final origin = object.footprint.origin;
      if (due > tick ||
          !_services.active.isActiveAt(
            origin.x.toDouble(),
            origin.y.toDouble(),
          )) {
        continue;
      }
      _objects.setState(object, nodeFull);
      _fillsAt.remove(object);
    }
  }
}
