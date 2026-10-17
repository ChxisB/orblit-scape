import 'action.dart';
import 'phase.dart';

/// What the features add at startup: action kinds and the systems that run in
/// each phase. The engine reads it and imports no feature.
final class Registry {
  final ActionQueue actions = ActionQueue();
  final Map<String, ActionKind> _kinds = {};
  final Map<Phase, List<void Function()>> _systems = {
    for (final phase in Phase.values) phase: [],
  };

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
