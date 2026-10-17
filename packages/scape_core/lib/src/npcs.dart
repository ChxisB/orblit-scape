import 'combat.dart';
import 'loot.dart';
import 'shops.dart';
import 'tile_coord.dart';

/// The name of the replicated component on a creature standing in the world.
const String npcName = 'Npc';

/// Ints in that component: the definition id.
const int npcInts = 1;

/// The highest NPC id.
const int maxNpcId = 0xFFFF;

/// The options of a creature, as the option byte of an `Interact` names them.
abstract final class NpcOption {
  static const int attack = 0;
  static const int trade = 1;
  static const int talk = 2;
}

/// What a creature does when nobody is making it do anything.
sealed class Behaviour {
  const Behaviour();
}

/// Stands where it was placed.
final class Idle extends Behaviour {
  const Idle();
}

/// Walks about within the radius its spawn names.
final class Wander extends Behaviour {
  const Wander();
}

/// Wanders until a player comes within [sight] tiles, then follows them. It
/// gives up when the player stands more than [leash] tiles from where the
/// creature was placed, and walks home.
final class Aggressive extends Behaviour {
  Aggressive({required this.sight, required this.leash}) {
    if (sight < 1) throw ArgumentError.value(sight, 'sight', 'is below one');
    if (leash < sight) {
      throw ArgumentError.value(leash, 'leash', 'is below the sight');
    }
  }

  final int sight;
  final int leash;
}

/// One kind of creature: a row of content.
final class NpcDef {
  NpcDef({
    required this.id,
    required this.name,
    this.behaviour = const Idle(),
    this.fight,
    this.loot,
    this.shop,
    this.dialogue,
  }) {
    if (id < 1 || id > maxNpcId) {
      throw ArgumentError.value(id, 'id', 'is outside 1 to $maxNpcId');
    }
    if (name.isEmpty) throw ArgumentError.value(name, 'name', 'is empty');
    if (dialogue != null && dialogue!.isEmpty) {
      throw ArgumentError.value(dialogue, 'dialogue', 'is empty');
    }
  }

  final int id;
  final String name;
  final Behaviour behaviour;

  /// How it fights, or null for a creature that cannot be attacked.
  final NpcFight? fight;

  /// What it drops when it dies, or null for nothing.
  final LootTable? loot;

  /// What it sells, or null for a creature that does not trade.
  final ShopDef? shop;

  /// The key of the conversation it opens, or null for a creature that does
  /// not talk.
  final String? dialogue;
}

/// The creatures a game has.
final class NpcSet {
  NpcSet({required List<NpcDef> npcs})
    : npcs = List.unmodifiable(npcs),
      _byId = {for (final npc in npcs) npc.id: npc} {
    if (_byId.length != npcs.length) {
      throw ArgumentError.value(npcs, 'npcs', 'repeats an id');
    }
  }

  final List<NpcDef> npcs;
  final Map<int, NpcDef> _byId;

  NpcDef? find(int id) => _byId[id];

  /// The creature called [id]. Throws [ArgumentError] when there is none, which
  /// is how content that names a missing creature fails at startup.
  NpcDef require(int id) =>
      _byId[id] ?? (throw ArgumentError.value(id, 'id', 'is not an NPC'));
}

/// A place a creature is put, and how far from it the creature may wander.
final class NpcSpawn {
  NpcSpawn({required this.npc, required this.tile, this.radius = 0}) {
    if (radius < 0) throw ArgumentError.value(radius, 'radius', 'is negative');
  }

  /// The id of the [NpcDef].
  final int npc;
  final TileCoord tile;
  final int radius;
}
