import 'package:scape_content/scape_content.dart';
import 'package:test/test.dart';

void main() {
  test('each gatherable object has a node and each node a different one', () {
    final objects = [ObjectId.tree, ObjectId.rock, ObjectId.fishingSpot];

    expect(defaultNodes.nodes, hasLength(objects.length));
    for (final id in objects) {
      expect(defaultNodes.find(id), isNotNull);
    }
  });

  test('a node needs a tool the first game has and gives an item it has', () {
    final tree = defaultNodes.find(ObjectId.tree)!;
    final rock = defaultNodes.find(ObjectId.rock)!;
    final spot = defaultNodes.find(ObjectId.fishingSpot)!;

    expect((tree.tool, tree.product), (ItemId.bronzeAxe, ItemId.logs));
    expect((rock.tool, rock.product), (ItemId.bronzePickaxe, ItemId.copperOre));
    expect((spot.tool, spot.product), (ItemId.fishingNet, ItemId.rawFish));
  });

  test('every node is gatherable at level 1 and gets easier by level 30', () {
    for (final node in defaultNodes.nodes) {
      expect(node.level, 1);
      expect(node.chanceAt(30), greaterThan(node.chanceAt(1)));
    }
  });
}
