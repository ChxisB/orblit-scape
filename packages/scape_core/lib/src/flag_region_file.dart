import 'dart:typed_data';

import 'flag_region.dart';
import 'tile_coord.dart';

/// The version a file is written in. A reader refuses any other.
const int flagRegionVersion = 1;

const List<int> _magic = [0x4f, 0x53, 0x46, 0x4c]; // "OSFL"
const int _headerLength = 10;

/// A region as bytes: the magic, a version, the plane, the region's x and y
/// as two bytes each (little-endian), then one flag byte a tile.
Uint8List encodeFlagRegion(FlagRegion region) {
  final bytes = Uint8List(_headerLength + region.flags.length);
  bytes.setRange(0, 4, _magic);
  bytes[4] = flagRegionVersion;
  bytes[5] = region.key.plane;
  ByteData.sublistView(bytes)
    ..setUint16(6, region.key.x, Endian.little)
    ..setUint16(8, region.key.y, Endian.little);
  bytes.setRange(_headerLength, bytes.length, region.flags);
  return bytes;
}

/// The region in [bytes]. Throws a [FormatException] for a file that is not
/// one, is cut short, is in another version or names a region outside the
/// world.
FlagRegion decodeFlagRegion(Uint8List bytes) {
  const fileLength = _headerLength + regionSize * regionSize;
  if (bytes.length != fileLength) {
    throw FormatException(
      'A region file is $fileLength bytes, not ${bytes.length}.',
    );
  }
  for (var i = 0; i < _magic.length; i++) {
    if (bytes[i] != _magic[i]) {
      throw const FormatException('This is not a region file.');
    }
  }
  if (bytes[4] != flagRegionVersion) {
    throw FormatException(
      'Region file version ${bytes[4]} is not read; this reads '
      'version $flagRegionVersion.',
    );
  }
  final view = ByteData.sublistView(bytes);
  final RegionKey key;
  try {
    key = RegionKey(
      view.getUint16(6, Endian.little),
      view.getUint16(8, Endian.little),
      bytes[5],
    );
  } on ArgumentError catch (e) {
    throw FormatException('The file names a region outside the world.', e);
  }
  return FlagRegion(key, Uint8List.fromList(bytes.sublist(_headerLength)));
}
