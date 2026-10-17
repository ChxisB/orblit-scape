import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('each creature id names one creature', () {
    for (final id in [NpcId.chicken, NpcId.goblin, NpcId.guard]) {
      expect(defaultNpcs.find(id), isNotNull);
    }
    expect(defaultNpcs.npcs, hasLength(3));
  });

  test('the creatures behave as their names say', () {
    expect(defaultNpcs.require(NpcId.chicken).behaviour, isA<Wander>());
    expect(defaultNpcs.require(NpcId.guard).behaviour, isA<Idle>());
    expect(defaultNpcs.require(NpcId.goblin).behaviour, isA<Aggressive>());
  });
}
