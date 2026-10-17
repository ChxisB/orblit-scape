import 'package:scape_core/scape_core.dart';

/// Open ground, two regions across and two up, on plane 0.
TileMap openWorld() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

final TileCoord spawnTile = TileCoord(32, 32, 0);
