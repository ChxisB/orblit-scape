import 'package:scape_content/scape_content.dart';
import 'package:test/test.dart';

void main() {
  test('a door is solid and offers Open when shut and Close when open', () {
    final door = defaultObjects.require(ObjectId.door);

    expect(door.solid, isTrue);
    expect(
      [
        for (final option in door.options)
          if (option.offeredIn(0)) option.label,
      ],
      ['Open'],
    );
    expect(
      [
        for (final option in door.options)
          if (option.offeredIn(1)) option.label,
      ],
      ['Close'],
    );
  });

  test('stairs are not solid and each has one way to climb', () {
    for (final id in [ObjectId.stairsUp, ObjectId.stairsDown]) {
      final stairs = defaultObjects.require(id);

      expect(stairs.solid, isFalse);
      expect([for (final option in stairs.options) option.action], ['climb']);
    }
  });

  test('a tree, a rock and a fishing spot are gathered only while full', () {
    for (final id in [ObjectId.tree, ObjectId.rock, ObjectId.fishingSpot]) {
      final node = defaultObjects.require(id);

      expect([for (final option in node.options) option.action], ['gather']);
      expect(node.options.single.offeredIn(0), isTrue);
      expect(node.options.single.offeredIn(1), isFalse);
    }
  });
}
