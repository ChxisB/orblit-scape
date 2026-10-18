import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart' show ActiveCells;
import 'package:scape_core/scape_core.dart';

/// What features need from the host to put an actor in the world and talk
/// to a player. Declared here, beside the features that call it.
abstract interface class Hosting {
  /// Makes [entity] replicate. [owner] is the player it belongs to.
  int spawn(int entity, {String? owner});

  void despawn(int entity);

  void tell(String player, ServerMessage message);

  /// Ends the session of [player] at once, as if they had logged out. Their
  /// save is written as for any leave. Completes when the session is gone.
  Future<void> kick(String player);
}

/// The parts of the server a feature may use while it registers.
final class Services {
  Services({
    required this.world,
    required this.map,
    required this.hosting,
    required this.active,
  });

  final World world;
  final TileMap map;
  final Hosting hosting;

  /// The cells worth simulating this tick, which are those near a player.
  /// A system that costs something per actor asks it first.
  final ActiveCells active;
}
