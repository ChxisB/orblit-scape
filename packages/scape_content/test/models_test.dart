import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

Look dressed(Map<String, int> worn) => Look(
  skin: 0xE0AC69,
  hair: 0x3B2F2F,
  cloth: 0x2F6FDB,
  worn: [for (final slot in equipmentSlots) worn[slot] ?? 0],
);

void main() {
  test('every creature has a model whose name carries a version', () {
    for (final npc in defaultNpcs.npcs) {
      final model = defaultModels.npc(npc.id);
      expect(model, isNotNull, reason: npc.name);
      expect(model!.name, matches(RegExp(r'^[a-z]+\.v\d+$')));
    }
    expect(defaultModels.npcIds.toSet(), {
      for (final npc in defaultNpcs.npcs) npc.id,
    });
  });

  test('no two creatures share a model name', () {
    final names = [
      for (final id in defaultModels.npcIds) defaultModels.npc(id)!.name,
    ];
    expect(names.toSet(), hasLength(names.length));
  });

  test('a creature with no model is null', () {
    expect(defaultModels.npc(9999), isNull);
  });

  test('every model has a pivot for each bone it moves', () {
    final models = [
      for (final id in defaultModels.npcIds) defaultModels.npc(id)!,
      defaultModels.player(dressed({})),
    ];
    for (final model in models) {
      for (final box in model.boxes) {
        expect(model.pivots, contains(box.bone), reason: model.name);
      }
    }
  });

  test('a smaller creature has its pivots at its own size', () {
    final goblin = defaultModels.npc(NpcId.goblin)!;
    final person = defaultModels.player(dressed({}));

    expect(
      goblin.pivots[ModelBone.armR]!.y,
      closeTo(person.pivots[ModelBone.armR]!.y * 0.8, 1e-9),
    );
  });

  group('a player', () {
    test('is the body alone with nothing worn', () {
      final model = defaultModels.player(dressed({}));

      expect(model.name, 'person.v1');
      expect(model.boxes, hasLength(7));
    });

    test('shows a part for each item worn, so equipping changes the model', () {
      final bare = defaultModels.player(dressed({}));
      final armed = defaultModels.player(
        dressed({'weapon': ItemId.bronzeSword}),
      );

      expect(armed.boxes.length, bare.boxes.length + 1);
      final sword = armed.boxes.last;
      expect(sword.bone, ModelBone.armR);
    });

    test('is drawn in the colours of the look', () {
      final model = defaultModels.player(dressed({}));

      expect(model.boxes.any((box) => box.colour == 0xE0AC69), isTrue);
      expect(model.boxes.any((box) => box.colour == 0x3B2F2F), isTrue);
      expect(model.boxes.any((box) => box.colour == 0x2F6FDB), isTrue);
    });

    test('shows a pair for legs and nothing for a ring', () {
      final legs = defaultModels.player(dressed({'legs': 3}));
      final ring = defaultModels.player(dressed({'ring': 3}));

      expect(legs.boxes.length, 7 + 2);
      expect(ring.boxes.length, 7);
    });

    test('shows two different items in one slot differently', () {
      final a = defaultModels.player(dressed({'weapon': 1}));
      final b = defaultModels.player(dressed({'weapon': 2}));

      expect(a.boxes.last.colour, isNot(b.boxes.last.colour));
    });

    test('has a part for every slot an item can fill, but the ring', () {
      for (final slot in equipmentSlots.where((s) => s != 'ring')) {
        final model = defaultModels.player(dressed({slot: 5}));
        expect(model.boxes.length, greaterThan(7), reason: slot);
      }
    });
  });
}
