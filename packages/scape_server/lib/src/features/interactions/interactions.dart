import 'package:scape_core/scape_core.dart';

import '../../engine/action.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../items/ground.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../npcs/npcs.dart';
import '../objects/objects.dart';

/// What a feature does when a player picks an option of an object.
typedef ObjectAction = void Function(String player, PlacedObject object);

/// What a feature does when [player] has walked up to the creature they
/// clicked and picked an option.
typedef NpcAction = void Function(String player, PlacedNpc npc);

/// Where [player] has to stand to pick an option on [npc].
typedef NpcReach = Reach Function(String player, PlacedNpc npc);

/// What a feature does when [player] has walked up to the player [other]
/// they clicked.
typedef PlayerAction = void Function(String player, String other);

/// What a feature does when [player] uses the item in [slot] on an object.
typedef UseOnObject =
    void Function(String player, int slot, PlacedObject object);

/// What a feature does when [player] uses the item in [slot] on the item in
/// [onSlot].
typedef UseOnItem = void Function(String player, int slot, int onSlot);

const String _cannotReach = 'You cannot reach that.';
const String _nothingHappens = 'Nothing interesting happens.';

const Map<Outcome, String> _pickUpNotices = {
  Outcome.partly: 'You only have room for some of it.',
  Outcome.wontFit: 'You have no room for that.',
  Outcome.nothingThere: 'It is gone.',
};

/// Clicking a thing does something. An interaction walks the player to where
/// they can reach it, then runs what the click names. A walk, or another
/// click, cancels one that has not yet run.
final class Interactions {
  Interactions._(
    this._registry,
    this._services, {
    required Movement movement,
    required WorldObjects objects,
    required Npcs npcs,
    required GroundItems ground,
    required Items items,
  }) : _movement = movement,
       _objects = objects,
       _npcs = npcs,
       _ground = ground,
       _items = items;

  /// Adds the interactions to [registry] and returns the feature, so a
  /// feature that owns an action can register its handler.
  factory Interactions.register(
    Registry registry,
    Services services, {
    required Movement movement,
    required WorldObjects objects,
    required Npcs npcs,
    required GroundItems ground,
    required Items items,
  }) {
    final feature = Interactions._(
      registry,
      services,
      movement: movement,
      objects: objects,
      npcs: npcs,
      ground: ground,
      items: items,
    );
    registry
      ..addKind(feature._interact)
      ..addIntentHandler(feature._onIntent);
    feature
      ..addObjectAction('toggle', (_, object) => feature._toggle(object))
      ..addObjectAction('climb', feature._climb);
    return feature;
  }

  final Registry _registry;
  final Services _services;
  final Movement _movement;
  final WorldObjects _objects;
  final Npcs _npcs;
  final GroundItems _ground;
  final Items _items;
  final ActionKind _interact = ActionKind(
    'interact',
    cancels: const CancelsAll(),
  );
  final Map<String, ObjectAction> _objectActions = {};
  final Map<(int, int), UseOnObject> _useOnObject = {};
  final Map<(int, int), UseOnItem> _useOnItem = {};
  final Map<int, ({NpcAction action, NpcReach? reach})> _npcActions = {};
  PlayerAction? _playerAction;

  /// Runs [action] when a player picks an option of an object whose
  /// definition names [name].
  void addObjectAction(String name, ObjectAction action) {
    if (_objectActions.containsKey(name)) {
      throw ArgumentError.value(name, 'name', 'is already registered');
    }
    _objectActions[name] = action;
  }

  /// Runs [action] when a player has walked up to a creature and picked
  /// [option], one of the [NpcOption] values. One feature owns each option,
  /// so a second call for it throws. The player walks up beside the creature
  /// unless [reach] says where else to stand.
  void addNpcAction(int option, NpcAction action, {NpcReach? reach}) {
    if (_npcActions.containsKey(option)) {
      throw StateError('That creature option already has an action.');
    }
    _npcActions[option] = (action: action, reach: reach);
  }

  /// Runs [action] when a player has walked up to another player and
  /// clicked them. One feature owns the click, so a second call throws.
  void setPlayerAction(PlayerAction action) {
    if (_playerAction != null) {
      throw StateError('A click on a player already has an action.');
    }
    _playerAction = action;
  }

  /// Runs [action] when [item] is used on an object whose definition is
  /// [object].
  void addUseOnObject(int item, int object, UseOnObject action) {
    if (_useOnObject.containsKey((item, object))) {
      throw ArgumentError('That use is already registered.');
    }
    _useOnObject[(item, object)] = action;
  }

  /// Runs [action] when [item] is used on [onItem], in either order. [action]
  /// is told the slot of [item] first.
  void addUseOnItem(int item, int onItem, UseOnItem action) {
    if (_useOnItem.containsKey(_pair(item, onItem))) {
      throw ArgumentError('That use is already registered.');
    }
    _useOnItem[_pair(item, onItem)] = (player, slot, onSlot) {
      final first = _held(player, slot)?.item == item;
      action(player, first ? slot : onSlot, first ? onSlot : slot);
    };
  }

  (int, int) _pair(int a, int b) => a < b ? (a, b) : (b, a);

  void _onIntent(String player, Intent intent) {
    if (intent is! Interact) return;
    switch (intent.kind) {
      case InteractKind.object:
        _clickObject(player, intent);
      case InteractKind.ground:
        _clickGround(player, intent);
      case InteractKind.useOnObject:
        _clickUseOnObject(player, intent);
      case InteractKind.useOnItem:
        _submit(player, (_) => _runUseOnItem(player, intent));
      case InteractKind.npc:
        _clickNpc(player, intent);
      case InteractKind.player:
        _clickPlayer(player, intent);
    }
  }

  void _clickObject(String player, Interact click) {
    final object = _objects.find(click.id);
    if (object == null || click.option >= object.def.options.length) return;
    _start(
      player,
      Beside(object.footprint),
      () => _runOption(player, object, click.option),
    );
  }

  void _clickNpc(String player, Interact click) {
    final npc = _npcs.find(click.id);
    final option = _npcActions[click.option];
    if (npc == null || option == null) return;
    _start(
      player,
      option.reach?.call(player, npc) ?? Beside(Footprint.single(npc.tile)),
      () => option.action(player, npc),
    );
  }

  void _clickPlayer(String player, Interact click) {
    final other = _movement.playerWith(click.id);
    final tile = other == null ? null : _movement.tileOf(other);
    final action = _playerAction;
    if (other == null || other == player || tile == null || action == null) {
      return;
    }
    _start(player, Beside(Footprint.single(tile)), () => action(player, other));
  }

  void _clickGround(String player, Interact click) {
    final tile = _ground.whereIs(player, click.id);
    if (tile == null) return;
    _start(player, OnTile(tile), () => _runTake(player, click.id));
  }

  void _clickUseOnObject(String player, Interact click) {
    final object = _objects.find(click.id);
    if (object == null || _held(player, click.option) == null) return;
    _start(
      player,
      Beside(object.footprint),
      () => _runUseOnObject(player, click.option, object),
    );
  }

  void _runOption(String player, PlacedObject object, int index) {
    final option = object.def.options[index];
    final action = _objectActions[option.action];
    if (action == null || !option.offeredIn(object.state)) {
      _tell(player, _nothingHappens);
      return;
    }
    action(player, object);
  }

  void _runTake(String player, int networkId) {
    final outcome = _ground.pickUp(player, networkId);
    if (outcome == Outcome.done) return;
    _tell(player, _pickUpNotices[outcome] ?? _cannotReach);
  }

  void _runUseOnObject(String player, int slot, PlacedObject object) {
    final held = _held(player, slot);
    final action = held == null
        ? null
        : _useOnObject[(held.item, object.def.id)];
    if (action == null) {
      _tell(player, _nothingHappens);
      return;
    }
    action(player, slot, object);
  }

  void _runUseOnItem(String player, Interact click) {
    final slot = click.option;
    final onSlot = click.id;
    final first = _held(player, slot);
    final second = _held(player, onSlot);
    final action = first == null || second == null || slot == onSlot
        ? null
        : _useOnItem[_pair(first.item, second.item)];
    if (action == null) {
      _tell(player, _nothingHappens);
      return;
    }
    action(player, slot, onSlot);
  }

  void _toggle(PlacedObject object) =>
      _objects.setState(object, object.state == 0 ? 1 : 0);

  void _climb(String player, PlacedObject object) {
    final to = object.destination;
    if (to == null) {
      _tell(player, _nothingHappens);
      return;
    }
    _movement.teleport(player, to);
  }

  ItemStack? _held(String player, int slot) =>
      _items.container(player, ContainerId.inventory)?.at(slot);

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(text));

  /// Queues [run] under the actor of [player], cancelling what that actor
  /// had waiting.
  void _submit(String player, void Function(int actor) run) {
    final actor = _movement.actorOf(player);
    if (actor == null) return;
    _registry.actions.submit(actor, _interact, () => run(actor));
  }

  /// Walks [player] to where [reach] holds, then runs [run].
  void _start(String player, Reach reach, void Function() run) =>
      _submit(player, (actor) => _begin(player, actor, reach, run));

  void _begin(String player, int actor, Reach reach, void Function() run) {
    final tile = _movement.tileOf(player);
    if (tile == null) return;
    _movement.follow(player, const []);
    if (tile.plane != reach.target.origin.plane) {
      _tell(player, _cannotReach);
    } else if (reachSatisfied(_services.map, reach, tile)) {
      run();
    } else {
      _approach(player, actor, reach, run);
    }
  }

  void _approach(String player, int actor, Reach reach, void Function() run) {
    final tile = _movement.tileOf(player)!; // _begin has just read it
    final path = findPathToReach(_services.map, tile, reach);
    if (!path.reached) {
      _tell(player, _cannotReach);
      return;
    }
    _movement.follow(player, path.steps);
    _wait(player, actor, reach, run);
  }

  /// Checks again next tick, queued so that a walk or a click cancels it.
  void _wait(String player, int actor, Reach reach, void Function() run) =>
      _registry.actions.submit(
        actor,
        _interact,
        () => _arrive(player, actor, reach, run),
      );

  void _arrive(String player, int actor, Reach reach, void Function() run) {
    final tile = _movement.tileOf(player);
    if (tile == null) return;
    if (reachSatisfied(_services.map, reach, tile)) {
      run();
    } else if (_movement.isMoving(player)) {
      _wait(player, actor, reach, run);
    } else {
      _tell(player, _cannotReach);
    }
  }
}
