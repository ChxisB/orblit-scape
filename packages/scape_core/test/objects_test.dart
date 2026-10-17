import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

ObjectOption option([String action = 'open', int? whenState]) =>
    ObjectOption(label: 'Open', action: action, whenState: whenState);

void main() {
  group('ObjectOption', () {
    test('with no state is offered in every state', () {
      expect(option().offeredIn(0), isTrue);
      expect(option().offeredIn(7), isTrue);
    });

    test('with a state is offered only in that state', () {
      final shut = option('open', 0);

      expect(shut.offeredIn(0), isTrue);
      expect(shut.offeredIn(1), isFalse);
    });

    test('refuses an empty label or action', () {
      expect(() => ObjectOption(label: '', action: 'a'), throwsArgumentError);
      expect(() => ObjectOption(label: 'a', action: ''), throwsArgumentError);
    });
  });

  group('ObjectDef', () {
    test('blocks its tiles in state 0 only, and only when solid', () {
      final tree = ObjectDef(id: 1, name: 'Tree', solid: true);
      final sign = ObjectDef(id: 2, name: 'Sign');

      expect(tree.blocksIn(0), isTrue);
      expect(tree.blocksIn(1), isFalse);
      expect(sign.blocksIn(0), isFalse);
    });

    test('gives a footprint of its size', () {
      final hall = ObjectDef(id: 1, name: 'Hall', width: 3, height: 2);

      final footprint = hall.footprintAt(TileCoord(10, 20, 0));

      expect(footprint.tiles, hasLength(6));
      expect(footprint.origin, TileCoord(10, 20, 0));
    });

    test('keeps its options out of reach of the caller', () {
      final list = [option()];
      final def = ObjectDef(id: 1, name: 'Door', options: list);

      list.clear();

      expect(def.options, hasLength(1));
      expect(() => def.options.add(option()), throwsUnsupportedError);
    });

    test('refuses an id, name, size or option count out of range', () {
      expect(() => ObjectDef(id: 0, name: 'A'), throwsArgumentError);
      expect(
        () => ObjectDef(id: maxObjectId + 1, name: 'A'),
        throwsArgumentError,
      );
      expect(() => ObjectDef(id: 1, name: ''), throwsArgumentError);
      expect(() => ObjectDef(id: 1, name: 'A', width: 0), throwsArgumentError);
      expect(() => ObjectDef(id: 1, name: 'A', height: 0), throwsArgumentError);
      expect(
        () => ObjectDef(id: 1, name: 'A', options: List.filled(256, option())),
        throwsArgumentError,
      );
    });
  });

  group('ObjectSet', () {
    test('finds and requires by id', () {
      final set = ObjectSet(objects: [ObjectDef(id: 3, name: 'Door')]);

      expect(set.find(3)?.name, 'Door');
      expect(set.find(4), isNull);
      expect(set.require(3).name, 'Door');
      expect(() => set.require(4), throwsArgumentError);
    });

    test('refuses a repeated id', () {
      expect(
        () => ObjectSet(
          objects: [
            ObjectDef(id: 1, name: 'A'),
            ObjectDef(id: 1, name: 'B'),
          ],
        ),
        throwsArgumentError,
      );
    });
  });

  test('TileFlags.without clears only the bits named', () {
    final flags = TileFlags.blocked | TileFlags.wallNorth;

    expect(flags.without(TileFlags.blocked).has(TileFlags.blocked), isFalse);
    expect(flags.without(TileFlags.blocked).has(TileFlags.wallNorth), isTrue);
    expect(TileFlags.open.without(TileFlags.blocked), TileFlags.open);
  });

  test('the interact kinds are distinct bytes', () {
    final kinds = {
      InteractKind.object,
      InteractKind.ground,
      InteractKind.useOnObject,
      InteractKind.useOnItem,
    };

    expect(kinds, hasLength(4));
    expect(kinds.every((k) => k >= 0 && k <= 0xFF), isTrue);
  });
}
