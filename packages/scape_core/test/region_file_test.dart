import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('a region round-trips through the file', () {
    final region = FlagRegion.open(RegionKey(3, 5, 2))
      ..setLocal(0, TileFlags.blocked)
      ..setLocal(65, TileFlags.wallNorth | TileFlags.water)
      ..setLocal(4095, TileFlags.noNpc);

    final back = decodeFlagRegion(encodeFlagRegion(region));

    expect(back.key, region.key);
    expect(back.flags, region.flags);
  });

  test('an unknown version is refused', () {
    final bytes = encodeFlagRegion(FlagRegion.open(RegionKey(0, 0, 0)));
    bytes[4] = flagRegionVersion + 1;
    expect(
      () => decodeFlagRegion(bytes),
      throwsA(
        isA<FormatException>().having(
          (e) => e.message,
          'message',
          contains('version'),
        ),
      ),
    );
  });

  test('a file with the wrong magic is refused', () {
    final bytes = encodeFlagRegion(FlagRegion.open(RegionKey(0, 0, 0)));
    bytes[0] = 0;
    expect(() => decodeFlagRegion(bytes), throwsFormatException);
  });

  test('a file of the wrong length is refused', () {
    final bytes = encodeFlagRegion(FlagRegion.open(RegionKey(0, 0, 0)));
    expect(
      () => decodeFlagRegion(Uint8List.sublistView(bytes, 0, bytes.length - 1)),
      throwsFormatException,
    );
    expect(() => decodeFlagRegion(Uint8List(3)), throwsFormatException);
  });

  test('a key outside the world is refused', () {
    final bytes = encodeFlagRegion(FlagRegion.open(RegionKey(0, 0, 0)));
    bytes[5] = planeCount;
    expect(() => decodeFlagRegion(bytes), throwsFormatException);
  });
}
