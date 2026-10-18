import 'session.dart';

enum DotKind { player, npc }

/// Where something is from the player, in tiles. East and north are positive.
typedef Dot = ({double dx, double dy, DotKind kind});

/// The players and creatures within [radius] tiles of the player, on their
/// plane, for a minimap to draw as dots. The player is the centre and not a
/// dot. There are no terrain flags here, because the client holds no map.
List<Dot> minimapDots(GameSession session, {int radius = 16}) {
  final me = session.actors.where((actor) => actor.mine).firstOrNull;
  if (me == null) return const [];
  Dot? near(double x, double y, int plane, DotKind kind) {
    final dx = x - me.x;
    final dy = y - me.y;
    final inside =
        plane == me.plane && dx.abs() <= radius && dy.abs() <= radius;
    return inside ? (dx: dx, dy: dy, kind: kind) : null;
  }

  return [
    for (final actor in session.actors)
      if (!actor.mine) ?near(actor.x, actor.y, actor.plane, DotKind.player),
    for (final npc in session.npcs) ?near(npc.x, npc.y, npc.plane, DotKind.npc),
  ];
}
