import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

Look aLook({int skin = 0xE0AC69, List<int>? worn}) => Look(
  skin: skin,
  hair: 0x3B2F2F,
  cloth: 0x2F6FDB,
  worn: worn ?? List.filled(equipmentSlots.length, 0),
);

void main() {
  group('an Anim', () {
    test('keeps its state through the bytes', () {
      final anim = Anim(base: ActorState.run, act: ActorState.hurt, seq: 7);
      final bytes = Uint8List(animInts * 4);

      writeAnim(bytes, anim);

      expect(tryReadAnim(bytes), anim);
    });

    test('has no act until one plays', () {
      final bytes = Uint8List(animInts * 4);
      writeAnim(bytes, Anim());

      expect(tryReadAnim(bytes)!.act, isNull);
    });

    test('counts each act, and the same act twice is a change', () {
      final once = Anim().playing(ActorState.attack);
      final twice = once.playing(ActorState.attack);

      expect(twice, isNot(once));
      expect(twice.seq, 2);
    });

    test('wraps its count back to zero', () {
      final last = Anim(act: ActorState.attack, seq: maxAnimSeq);

      expect(last.playing(ActorState.attack).seq, 0);
    });

    test('keeps the act when the resting state changes', () {
      final anim = Anim().playing(ActorState.gather).withBase(ActorState.walk);

      expect(
        (anim.base, anim.act, anim.seq),
        (ActorState.walk, ActorState.gather, 1),
      );
    });

    test('refuses an act as the resting state and the reverse', () {
      expect(() => Anim(base: ActorState.attack), throwsArgumentError);
      expect(() => Anim(act: ActorState.walk), throwsArgumentError);
      expect(() => Anim(seq: -1), throwsRangeError);
      expect(() => Anim(seq: maxAnimSeq + 1), throwsRangeError);
    });

    test('reads bytes that name no state as null', () {
      for (final ints in [
        [9, -1, 0],
        [-1, -1, 0],
        [0, 2, 0],
        [0, 7, 0],
        [0, -2, 0],
        [0, -1, -1],
      ]) {
        final bytes = Uint8List(animInts * 4);
        final data = ByteData.sublistView(bytes);
        for (final (i, value) in ints.indexed) {
          data.setInt32(i * 4, value, Endian.little);
        }
        expect(tryReadAnim(bytes), isNull, reason: '$ints');
      }
    });
  });

  group('a Look', () {
    test('keeps its colours and worn items through the bytes', () {
      final worn = List.generate(equipmentSlots.length, (slot) => slot * 3);
      final look = aLook(worn: worn);
      final bytes = Uint8List(lookInts * 4);

      writeLook(bytes, look);

      expect(tryReadLook(bytes), look);
      expect(tryReadLook(bytes)!.worn, worn);
    });

    test('is not the same look when one slot differs', () {
      final worn = List.filled(equipmentSlots.length, 0)..[3] = 12;

      expect(aLook(worn: worn), isNot(aLook()));
      expect(aLook().withWorn(worn), aLook(worn: worn));
    });

    test('cannot be changed through the list it was made with', () {
      final worn = List.filled(equipmentSlots.length, 0);
      final look = aLook(worn: worn);

      worn[0] = 5;

      expect(look.worn[0], 0);
      expect(() => look.worn[0] = 5, throwsUnsupportedError);
    });

    test('refuses a colour, an item or a count out of range', () {
      expect(() => aLook(skin: 0x1000000), throwsRangeError);
      expect(() => aLook(skin: -1), throwsRangeError);
      expect(() => aLook(worn: [1, 2]), throwsArgumentError);
      expect(
        () => aLook(worn: List.filled(equipmentSlots.length, maxItemId + 1)),
        throwsRangeError,
      );
    });

    test('reads bytes that are not a look as null', () {
      final bytes = Uint8List(lookInts * 4);
      writeLook(bytes, aLook());
      ByteData.sublistView(bytes).setInt32(16, -4, Endian.little);

      expect(tryReadLook(bytes), isNull);
    });
  });
}
