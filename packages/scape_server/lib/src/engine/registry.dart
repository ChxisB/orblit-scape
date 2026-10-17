import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import 'action.dart';
import 'phase.dart';

typedef IntentHandler = void Function(String player, Intent intent);

/// What the features add at startup: action kinds, the systems that run in
/// each phase, the components they replicate and the handlers for intents and
/// joins. The engine reads it and imports no feature.
final class Registry {
  final ActionQueue actions = ActionQueue();
  final Map<String, ActionKind> _kinds = {};
  final Map<Phase, List<void Function()>> _systems = {
    for (final phase in Phase.values) phase: [],
  };

  final List<ComponentType> _replicated = [];
  final List<IntentHandler> _intentHandlers = [];
  final List<void Function(String player)> _joinHandlers = [];
  final List<void Function(String player)> _goneHandlers = [];

  /// The components the host sends to clients, in the order they were added.
  List<ComponentType> get replicated => List.unmodifiable(_replicated);

  void replicate(ComponentType component) => _replicated.add(component);

  void addIntentHandler(IntentHandler handler) => _intentHandlers.add(handler);

  void addJoinHandler(void Function(String player) handler) =>
      _joinHandlers.add(handler);

  void addGoneHandler(void Function(String player) handler) =>
      _goneHandlers.add(handler);

  /// Offers an intent to every handler. Each one picks out what it owns.
  void deliver(String player, Intent intent) {
    for (final handler in _intentHandlers) {
      handler(player, intent);
    }
  }

  void playerJoined(String player) {
    for (final handler in _joinHandlers) {
      handler(player);
    }
  }

  void playerGone(String player) {
    for (final handler in _goneHandlers) {
      handler(player);
    }
  }

  void addKind(ActionKind kind) {
    if (_kinds.containsKey(kind.name)) {
      throw ArgumentError.value(kind.name, 'kind', 'is already registered');
    }
    _kinds[kind.name] = kind;
  }

  ActionKind? kindNamed(String name) => _kinds[name];

  void addSystem(Phase phase, void Function() system) =>
      _systems[phase]!.add(system);

  Iterable<void Function()> systemsFor(Phase phase) => _systems[phase]!;
}
