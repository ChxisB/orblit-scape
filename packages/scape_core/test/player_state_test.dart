import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/items.dart';

List<ItemStack?> slotsOf(ContainerId id, Map<int, ItemStack> held) => [
  for (var slot = 0; slot < containerSize(id); slot++) held[slot],
];

PlayerState sample() => PlayerState(
  tile: TileCoord(40, 41, 1),
  skills: {'fighting': 1200, 'cooking': 0},
  containers: {
    ContainerId.inventory: slotsOf(ContainerId.inventory, {
      0: ItemStack(coins, 99),
      27: ItemStack(logs, 1),
    }),
    ContainerId.bank: slotsOf(ContainerId.bank, {799: ItemStack(sword, 1)}),
  },
  friends: ['bo', 'cy'],
  ignored: ['dee'],
);

Uint8List json(Object? value) =>
    Uint8List.fromList(utf8.encode(jsonEncode(value)));

Map<String, Object?> parsed(PlayerState state) =>
    jsonDecode(utf8.decode(state.encode())) as Map<String, Object?>;

void main() {
  group('a state', () {
    test('round-trips every part', () {
      final back = PlayerState.decode(sample().encode());

      expect(back.tile, TileCoord(40, 41, 1));
      expect(back.skills, {'fighting': 1200, 'cooking': 0});
      expect(back.friends, ['bo', 'cy']);
      expect(back.ignored, ['dee']);
      expect(
        back.containers[ContainerId.inventory],
        sample().containers[ContainerId.inventory],
      );
      expect(back.containers[ContainerId.bank]![799], ItemStack(sword, 1));
      expect(back.containers.containsKey(ContainerId.equipment), isFalse);
    });

    test('with nothing but a tile round-trips', () {
      final back = PlayerState.decode(
        PlayerState(tile: TileCoord(1, 2, 0)).encode(),
      );

      expect(back.tile, TileCoord(1, 2, 0));
      expect(back.skills, isEmpty);
      expect(back.containers, isEmpty);
      expect(back.friends, isEmpty);
    });

    test('encodes the same bytes whatever order it was built in', () {
      final other = PlayerState(
        tile: TileCoord(40, 41, 1),
        skills: {'cooking': 0, 'fighting': 1200},
        containers: {
          ContainerId.bank: sample().containers[ContainerId.bank]!,
          ContainerId.inventory: sample().containers[ContainerId.inventory]!,
        },
        friends: ['bo', 'cy'],
        ignored: ['dee'],
      );

      expect(other.encode(), sample().encode());
    });

    test('writes only the slots that hold something', () {
      final inventory =
          (parsed(sample())['containers']!
              as Map<String, Object?>)['inventory'];

      expect(inventory, [
        [0, coins, 99],
        [27, logs, 1],
      ]);
    });

    test('cannot be changed through what it hands out', () {
      final state = sample();
      expect(() => state.skills['x'] = 1, throwsUnsupportedError);
      expect(() => state.friends.add('x'), throwsUnsupportedError);
      expect(
        () => state.containers[ContainerId.inventory]![5] = ItemStack(logs, 1),
        throwsUnsupportedError,
      );
    });

    test('keeps what it was built from even if that changes afterwards', () {
      final skills = {'fighting': 5};
      final state = PlayerState(tile: TileCoord(1, 1, 0), skills: skills);
      skills['fighting'] = 6;

      expect(state.skills['fighting'], 5);
    });
  });

  group('building a state', () {
    final tile = TileCoord(1, 1, 0);

    test('refuses experience outside 0 to the 32 bit limit', () {
      expect(
        () => PlayerState(tile: tile, skills: {'a': -1}),
        throwsArgumentError,
      );
      expect(
        () => PlayerState(tile: tile, skills: {'a': maxSavedXp + 1}),
        throwsArgumentError,
      );
      expect(
        PlayerState(tile: tile, skills: {'a': maxSavedXp}).skills['a'],
        maxSavedXp,
      );
    });

    test('refuses a skill with no id', () {
      expect(
        () => PlayerState(tile: tile, skills: {'': 1}),
        throwsArgumentError,
      );
    });

    test('refuses a container of the wrong size', () {
      expect(
        () => PlayerState(
          tile: tile,
          containers: {ContainerId.inventory: List.filled(27, null)},
        ),
        throwsArgumentError,
      );
    });

    test('refuses a name that is not a player key', () {
      expect(
        () => PlayerState(tile: tile, friends: ['Bo']),
        throwsArgumentError,
      );
      expect(
        () => PlayerState(tile: tile, ignored: ['no way too long']),
        throwsArgumentError,
      );
    });

    test('refuses a list over the limit', () {
      final many = [for (var i = 0; i <= maxListEntries; i++) 'p$i'];
      expect(() => PlayerState(tile: tile, friends: many), throwsArgumentError);
      expect(
        PlayerState(tile: tile, friends: many.take(maxListEntries)).friends,
        hasLength(maxListEntries),
      );
    });
  });

  group('reading a state', () {
    Map<String, Object?> good() => parsed(sample());

    void refuses(String why, Uint8List bytes) => expect(
      () => PlayerState.decode(bytes),
      throwsFormatException,
      reason: why,
    );

    test('refuses what is not JSON or not an object', () {
      refuses('text', Uint8List.fromList(utf8.encode('hello')));
      refuses('list', json([1, 2]));
      refuses('empty', Uint8List(0));
      refuses('bad utf8', Uint8List.fromList([0xff, 0xfe]));
    });

    test('refuses a missing, odd or later version', () {
      refuses('missing', json(good()..remove('version')));
      refuses('zero', json(good()..['version'] = 0));
      refuses('text', json(good()..['version'] = '1'));
      refuses('later', json(good()..['version'] = playerStateVersion + 1));
    });

    test('says which version came from a later server', () {
      expect(
        () => PlayerState.decode(json(good()..['version'] = 99)),
        throwsA(
          isA<FormatException>().having(
            (e) => e.message,
            'message',
            contains('later'),
          ),
        ),
      );
    });

    test('refuses a bad tile', () {
      refuses('missing', json(good()..remove('tile')));
      refuses('short', json(good()..['tile'] = [1, 2]));
      refuses('text', json(good()..['tile'] = [1, 2, 'a']));
      refuses('plane', json(good()..['tile'] = [1, 2, 99]));
      refuses('negative', json(good()..['tile'] = [-1, 2, 0]));
    });

    test('refuses a skill that is not a whole number or is negative', () {
      refuses('text', json(good()..['skills'] = {'a': 'x'}));
      refuses('fraction', json(good()..['skills'] = {'a': 1.5}));
      refuses('negative', json(good()..['skills'] = {'a': -1}));
      refuses('list', json(good()..['skills'] = [1]));
    });

    test('refuses an unknown container and a bad slot', () {
      refuses('name', json(good()..['containers'] = {'chest': <Object?>[]}));
      for (final slot in [
        [-1, coins, 1],
        [inventorySize, coins, 1],
        [0, 0, 1],
        [0, coins, 0],
        [0, coins, maxStack + 1],
        [0, coins],
        ['0', coins, 1],
      ]) {
        refuses(
          '$slot',
          json(
            good()
              ..['containers'] = {
                'inventory': [slot],
              },
          ),
        );
      }
    });

    test('refuses a slot named twice', () {
      refuses(
        'twice',
        json(
          good()
            ..['containers'] = {
              'inventory': [
                [0, coins, 1],
                [0, logs, 1],
              ],
            },
        ),
      );
    });

    test('refuses lists that hold something else than player keys', () {
      refuses('number', json(good()..['friends'] = [1]));
      refuses('upper', json(good()..['friends'] = ['Bo']));
      refuses('not a list', json(good()..['ignored'] = 'bo'));
    });

    test('reads a state with only the version and a tile', () {
      final back = PlayerState.decode(
        json({
          'version': 1,
          'tile': [3, 4, 0],
        }),
      );
      expect(back.tile, TileCoord(3, 4, 0));
    });

    test('random damage never throws anything but a FormatException', () {
      final random = Random(11);
      final bytes = sample().encode();
      for (var i = 0; i < 3000; i++) {
        final damaged = Uint8List.fromList(bytes);
        for (var hit = 0; hit < 1 + random.nextInt(4); hit++) {
          damaged[random.nextInt(damaged.length)] = random.nextInt(256);
        }
        final cut = random.nextBool()
            ? damaged.length
            : random.nextInt(damaged.length);
        try {
          PlayerState.decode(Uint8List.sublistView(damaged, 0, cut));
        } on FormatException {
          // refused, which is the point
        }
      }
    });
  });
}
