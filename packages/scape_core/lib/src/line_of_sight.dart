import 'tile_coord.dart';
import 'tile_flags.dart';
import 'tile_map.dart';

/// Whether something thrown from [from] to [to] passes over every tile
/// between them.
///
/// The line is the tiles a straight line between the two centres crosses. It
/// stops at a blocked tile, which the last tile may be, and at a wall on any
/// edge it crosses. Water does not stop it. A diagonal step crosses both of
/// its corner edges.
bool hasClearLine(TileMap map, TileCoord from, TileCoord to) {
  var x = from.x;
  var y = from.y;
  final dx = (to.x - x).abs();
  final dy = (to.y - y).abs();
  final sx = to.x > x ? 1 : -1;
  final sy = to.y > y ? 1 : -1;
  var error = dx - dy;
  var here = from;
  while (here != to) {
    final doubled = 2 * error;
    if (doubled > -dy) {
      error -= dy;
      x += sx;
    }
    if (doubled < dx) {
      error += dx;
      y += sy;
    }
    final next = TileCoord(x, y, from.plane);
    if (next != to && map.flagsAt(next).has(TileFlags.blocked)) return false;
    if (!_crosses(map, here, next)) return false;
    here = next;
  }
  return true;
}

bool _crosses(TileMap map, TileCoord a, TileCoord b) {
  if (a.x == b.x || a.y == b.y) return map.edgeOpen(a, b);
  final alongX = TileCoord(b.x, a.y, a.plane);
  final alongY = TileCoord(a.x, b.y, a.plane);
  return map.edgeOpen(a, alongX) &&
      map.edgeOpen(alongX, b) &&
      map.edgeOpen(a, alongY) &&
      map.edgeOpen(alongY, b);
}
