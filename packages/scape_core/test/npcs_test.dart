import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  group('Aggressive', () {
    test('needs a sight of at least one tile', () {
      expect(() => Aggressive(sight: 0, leash: 5), throwsArgumentError);
    });

    test('needs a leash no shorter than its sight', () {
      expect(() => Aggressive(sight: 5, leash: 4), throwsArgumentError);
      expect(Aggressive(sight: 5, leash: 5).leash, 5);
    });
  });

  group('NpcDef', () {
    test('stands still unless told otherwise', () {
      expect(NpcDef(id: 1, name: 'Guard').behaviour, isA<Idle>());
    });

    test('rejects an id outside range and an empty name', () {
      expect(() => NpcDef(id: 0, name: 'A'), throwsArgumentError);
      expect(() => NpcDef(id: maxNpcId + 1, name: 'A'), throwsArgumentError);
      expect(() => NpcDef(id: maxNpcId, name: 'A'), returnsNormally);
      expect(() => NpcDef(id: 1, name: ''), throwsArgumentError);
    });

    test('names a conversation or none, never an empty key', () {
      expect(NpcDef(id: 1, name: 'A').dialogue, isNull);
      expect(NpcDef(id: 1, name: 'A', dialogue: 'a').dialogue, 'a');
      expect(() => NpcDef(id: 1, name: 'A', dialogue: ''), throwsArgumentError);
    });
  });

  group('NpcSet', () {
    final set = NpcSet(npcs: [NpcDef(id: 1, name: 'Guard')]);

    test('finds a creature by id', () {
      expect(set.find(1)!.name, 'Guard');
      expect(set.find(2), isNull);
    });

    test('require throws for a creature that does not exist', () {
      expect(set.require(1).id, 1);
      expect(() => set.require(2), throwsArgumentError);
    });

    test('rejects a repeated id', () {
      expect(
        () => NpcSet(
          npcs: [
            NpcDef(id: 1, name: 'A'),
            NpcDef(id: 1, name: 'B'),
          ],
        ),
        throwsArgumentError,
      );
    });
  });

  group('NpcSpawn', () {
    final tile = TileCoord(1, 1, 0);

    test('has no radius by default and rejects a negative one', () {
      expect(NpcSpawn(npc: 1, tile: tile).radius, 0);
      expect(
        () => NpcSpawn(npc: 1, tile: tile, radius: -1),
        throwsArgumentError,
      );
    });
  });
}
