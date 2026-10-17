import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

/// What features need from the host to put an actor in the world and talk
/// to a player. Declared here, beside the features that call it.
abstract interface class Hosting {
  /// Makes [entity] replicate. [owner] is the player it belongs to.
  int spawn(int entity, {String? owner});

  void despawn(int entity);

  void tell(String player, ServerMessage message);
}

/// The parts of the server a feature may use while it registers.
final class Services {
  Services({required this.world, required this.map, required this.hosting});

  final World world;
  final TileMap map;
  final Hosting hosting;
}
