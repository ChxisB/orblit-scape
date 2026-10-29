import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

/// The window of random ground every map case is cut from. The rest of the
/// region round it is blocked.
const _low = 20;
const _size = 12;

const _walls = [
  TileFlags.wallNorth,
  TileFlags.wallEast,
  TileFlags.wallSouth,
  TileFlags.wallWest,
];

TileCoord _at(int x, int y) => TileCoord(x, y, 0);

TileCoord _inWindow(Random random) =>
    _at(_low + random.nextInt(_size), _low + random.nextInt(_size));

List<int> _xy(TileCoord tile) => [tile.x, tile.y];

/// Random flags for each tile of the window, row by row from its south-west
/// corner. Dart and C# seed a random source differently, so the flags travel
/// in the fixture.
List<int> _window(Random random) => [
  for (var i = 0; i < _size * _size; i++) _flags(random).bits,
];

TileFlags _flags(Random random) {
  var flags = TileFlags.open;
  if (random.nextInt(100) < 22) flags |= TileFlags.blocked;
  if (random.nextInt(100) < 5) flags |= TileFlags.water;
  if (random.nextInt(100) < 8) flags |= TileFlags.noNpc;
  for (final wall in _walls) {
    if (random.nextInt(100) < 6) flags |= wall;
  }
  return flags;
}

TileMap _map(List<int> window) {
  final region = FlagRegion.open(RegionKey(0, 0, 0));
  for (var i = 0; i < regionSize * regionSize; i++) {
    region.setLocal(i, TileFlags.blocked);
  }
  final map = TileMap()..addRegion(region);
  for (var i = 0; i < window.length; i++) {
    final tile = _at(_low + i % _size, _low + i ~/ _size);
    map.setFlags(tile, TileFlags(window[i]));
  }
  return map;
}

/// A reach of a random kind round a random footprint in the window, and the
/// row that names it.
(Reach, _Json) _reach(Random random) {
  final origin = _inWindow(random);
  final kind = ['on', 'beside', 'within'][random.nextInt(3)];
  final width = kind == 'on' ? 1 : 1 + random.nextInt(3);
  final height = kind == 'on' ? 1 : 1 + random.nextInt(3);
  final range = 1 + random.nextInt(6);
  final target = Footprint(origin, width, height);
  final Reach reach = switch (kind) {
    'on' => OnTile(origin),
    'beside' => Beside(target),
    _ => Within(target, range: range),
  };
  return (
    reach,
    {
      'kind': kind,
      'origin': _xy(origin),
      'width': width,
      'height': height,
      'range': range,
    },
  );
}

/// A search over a random window. The steps pin the order the search tries
/// its neighbours in, which the shortest length alone does not.
_Json _path(Random random) {
  final window = _window(random);
  final from = _inWindow(random);
  final (reach, row) = _reach(random);
  final walker = random.nextBool() ? Walker.npc : Walker.player;
  final margin = random.nextInt(4) == 0 ? 2 : defaultPathMargin;
  final result = findPathToReach(
    _map(window),
    from,
    reach,
    walker: walker,
    margin: margin,
  );
  return {
    'op': 'path',
    'window': window,
    'from': _xy(from),
    'reach': row,
    'walker': walker.name,
    'margin': margin,
    'expect': {
      'reached': result.reached,
      'steps': [for (final step in result.steps) _xy(step)],
    },
  };
}

/// Lines between pairs of tiles in a random window.
_Json _sight(Random random) {
  final window = _window(random);
  final map = _map(window);
  final pairs = [
    for (var i = 0; i < 8; i++) [
      ..._xy(_inWindow(random)),
      ..._xy(_inWindow(random)),
    ],
  ];
  return {
    'op': 'sight',
    'window': window,
    'pairs': pairs,
    'expect': [
      for (final p in pairs) hasClearLine(map, _at(p[0], p[1]), _at(p[2], p[3])),
    ],
  };
}

_Json _encode(List<int> key, List<List<int>> set) {
  final region = FlagRegion.open(RegionKey(key[0], key[1], key[2]));
  for (final [index, bits] in set) {
    region.setLocal(index, TileFlags(bits));
  }
  return {
    'op': 'encode',
    'key': key,
    'set': set,
    'expect': hex(encodeFlagRegion(region)),
  };
}

/// An open region's file with [edits] made to its bytes, cut to [length].
_Json _decode({List<List<int>> edits = const [], int? length}) {
  final bytes = encodeFlagRegion(FlagRegion.open(RegionKey(0, 0, 0)));
  for (final [at, value] in edits) {
    bytes[at] = value;
  }
  final cut = Uint8List.sublistView(bytes, 0, length ?? bytes.length);
  return {
    'op': 'decode',
    'edits': edits,
    'length': cut.length,
    'expect': _refusal(() {
      final region = decodeFlagRegion(cut);
      final key = region.key;
      return [key.x, key.y, key.plane, region.flags[0], region.flags[4095]];
    }),
  };
}

/// What a read gave, or the message it was refused with. The messages are
/// the reader's own words, so they are compared.
_Json _refusal(Object? Function() body) {
  try {
    return {'value': body()};
  } on FormatException catch (e) {
    return {'refused': e.message};
  }
}

_Json _position(int x, int y, int plane) {
  final bytes = Uint8List(tilePositionFloats * 4);
  writeTilePosition(bytes, TileCoord(x, y, plane));
  return {
    'op': 'position',
    'tile': [x, y, plane],
    'expect': hex(bytes),
  };
}

/// A component holding [x], [plane] and [y] as written, which need not be
/// whole.
_Json _read(double x, double plane, double y) {
  final bytes = Uint8List(12);
  ByteData.sublistView(bytes)
    ..setFloat32(0, x, Endian.little)
    ..setFloat32(4, plane, Endian.little)
    ..setFloat32(8, y, Endian.little);
  final point = readTilePoint(bytes);
  return {
    'op': 'read',
    'bytes': hex(bytes),
    'expect': [number(point.x), number(point.y), point.plane],
  };
}

_Json _nearest(double x, double y) => {
  'op': 'nearest',
  'point': [number(x), number(y)],
  'expect': attempt(() {
    final tile = tileNearest((x: x, y: y, plane: 0));
    return [tile.x, tile.y];
  }),
};

/// Usage: `dart run bin/core_world.dart <repository root>`.
void main(List<String> args) {
  final random = Random(7);
  writeFixture(
    root: args.single,
    package: 'scape_core',
    name: 'world',
    cases: [
      for (var i = 0; i < 120; i++) _path(random),
      for (var i = 0; i < 30; i++) _sight(random),
      _encode([3, 5, 2], [
        [0, 1],
        [65, 34],
        [4095, 64],
      ]),
      _encode([255, 255, 3], [
        [1, 127],
        [4032, 2],
      ]),
      _decode(),
      _decode(
        edits: [
          [10, 33],
          [4105, 64],
        ],
      ),
      _decode(edits: [[0, 0]]),
      _decode(edits: [[4, 2]]),
      _decode(edits: [[5, 4]]),
      _decode(
        edits: [
          [6, 0],
          [7, 1],
        ],
      ),
      _decode(
        edits: [
          [8, 255],
          [9, 0],
        ],
      ),
      _decode(length: 4105),
      _decode(length: 3),
      _decode(length: 0),
      _position(0, 0, 0),
      _position(16383, 12, 3),
      _position(5, 9, 2),
      _read(3.25, 2.5, 7.75),
      _read(16383, 1.5, 0.5),
      _read(10.5, 0.49, 2),
      _nearest(2.5, 3.5),
      _nearest(0.5, 7.49),
      _nearest(16383.49, 1),
      _nearest(16383.5, 1),
      _nearest(-0.5, 0),
      _nearest(-0.49, 0),
      _nearest(double.nan, 0),
    ],
  );
}
