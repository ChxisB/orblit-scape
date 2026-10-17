import 'dart:typed_data';

import 'tile_coord.dart';
import 'tile_flags.dart';

/// The flags of one region: [regionSize] squared tiles, row by row from the
/// south-west corner, one byte each.
final class FlagRegion {
  /// A region with nothing on it.
  FlagRegion.open(this.key) : flags = Uint8List(regionSize * regionSize);

  /// A region over [flags], which must hold one byte a tile.
  FlagRegion(this.key, this.flags) {
    if (flags.length != regionSize * regionSize) {
      throw ArgumentError.value(flags.length, 'flags', 'One byte a tile.');
    }
  }

  final RegionKey key;
  final Uint8List flags;

  TileFlags flagsAtLocal(int index) => TileFlags(flags[index]);

  void setLocal(int index, TileFlags value) => flags[index] = value.bits;
}
