import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// What the server holds about one player.
typedef PlayerView = ({
  TileCoord tile,
  bool moving,
  bool fighting,
  ({int now, int max})? health,
  Map<String, int> experience,
});

/// A creature or object in an observed area, by its definition id.
typedef Seen = ({int networkId, int def, TileCoord tile});

/// What stands in an observed area.
typedef AreaView = ({
  List<String> players,
  List<Seen> creatures,
  List<Seen> objects,
});

/// The most tiles from the centre an observed area may reach. The area is
/// read tile by tile.
const int maxAreaRadius = 32;

/// Lets a loopback link deliver everything queued, and what that causes.
Future<void> deliver() async {
  for (var i = 0; i < 4; i++) {
    await Future<void>.delayed(Duration.zero);
  }
}

/// The tools an agent uses to look at a running [ScapeServer] and to move it
/// along: who is there, what a player holds, what stands in an area, and how
/// long the ticks took. It reads the server's own state, so it sees what the
/// server knows and not what a client was told.
final class ServerProbe {
  ServerProbe(this._server);

  final ScapeServer _server;
  final List<Duration> _tickTimes = [];

  /// The length of each tick that [step] ran, oldest first.
  List<Duration> get tickTimes => List.unmodifiable(_tickTimes);

  /// The players in the world, by name, in the order they joined.
  List<String> players() => _server.movement.players.toList();

  /// What the server holds about [name], or null when they are not in the
  /// world.
  PlayerView? player(String name) {
    final standing = _server.movement.standing(name);
    if (standing == null) return null;
    return (
      tile: standing.tile,
      moving: _server.movement.isMoving(name),
      fighting: _server.combat.isFighting(name),
      health: _server.combat.healthOfPlayer(name),
      experience: _server.skills.experienceOf(name),
    );
  }

  /// The players, creatures and objects within [radius] tiles of [centre].
  /// An object that covers several tiles is listed once, at the first tile
  /// found.
  AreaView area(TileCoord centre, int radius) {
    RangeError.checkValueInInterval(radius, 0, maxAreaRadius, 'radius');
    bool within(TileCoord tile) =>
        tile.plane == centre.plane &&
        (tile.x - centre.x).abs() <= radius &&
        (tile.y - centre.y).abs() <= radius;
    return (
      players: [
        for (final name in players())
          if (_server.movement.standing(name) case final at?
              when within(at.tile))
            name,
      ],
      creatures: [
        for (final npc in _server.npcs.standing)
          if (within(npc.tile))
            (networkId: npc.networkId, def: npc.def.id, tile: npc.tile),
      ],
      objects: _objectsIn(centre, radius),
    );
  }

  List<Seen> _objectsIn(TileCoord centre, int radius) {
    final found = <int, Seen>{};
    for (var y = centre.y - radius; y <= centre.y + radius; y++) {
      for (var x = centre.x - radius; x <= centre.x + radius; x++) {
        if (x < 0 || y < 0 || x >= worldSize || y >= worldSize) continue;
        final tile = TileCoord(x, y, centre.plane);
        if (_server.objects.at(tile) case final object?) {
          found.putIfAbsent(
            object.networkId,
            () => (networkId: object.networkId, def: object.def.id, tile: tile),
          );
        }
      }
    }
    return found.values.toList();
  }

  /// Runs [ticks] ticks, timing each one. What clients sent is delivered
  /// before a tick and what the tick sent is delivered after it.
  Future<void> step(int ticks) async {
    RangeError.checkNotNegative(ticks, 'ticks');
    final watch = Stopwatch();
    for (var i = 0; i < ticks; i++) {
      await deliver();
      watch
        ..reset()
        ..start();
      _server.loop.runTick();
      watch.stop();
      _tickTimes.add(watch.elapsed);
      await deliver();
    }
  }
}
