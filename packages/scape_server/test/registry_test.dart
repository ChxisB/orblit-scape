import 'package:scape_server/src/engine/action.dart';
import 'package:scape_server/src/engine/phase.dart';
import 'package:scape_server/src/engine/registry.dart';
import 'package:test/test.dart';

void main() {
  test('finds a kind by the name it was registered under', () {
    final walk = ActionKind('walk');
    final registry = Registry()..addKind(walk);
    expect(registry.kindNamed('walk'), same(walk));
    expect(registry.kindNamed('run'), isNull);
  });

  test('refuses a second kind with the same name', () {
    final registry = Registry()..addKind(ActionKind('walk'));
    expect(() => registry.addKind(ActionKind('walk')), throwsArgumentError);
  });

  test('keeps the systems of a phase in the order they were added', () {
    final order = <int>[];
    final registry = Registry()
      ..addSystem(Phase.movement, () => order.add(1))
      ..addSystem(Phase.combat, () => order.add(9))
      ..addSystem(Phase.movement, () => order.add(2));
    for (final system in registry.systemsFor(Phase.movement)) {
      system();
    }
    expect(order, [1, 2]);
    expect(registry.systemsFor(Phase.npcs), isEmpty);
  });
}
