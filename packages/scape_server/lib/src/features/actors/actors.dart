import 'package:orblit_core/orblit_core.dart';
import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../npcs/npcs.dart';

const List<int> _skins = [0xF1C27D, 0xE0AC69, 0xC68642, 0x8D5524, 0x5C3A1E];
const List<int> _hairs = [0x0F0A07, 0x3B2F2F, 0x6A4A2A, 0xB8863B, 0xA0A0A0];
const List<int> _cloths = [
  0x2F6FDB,
  0xB23A3A,
  0x3A8F4F,
  0x8A5BB5,
  0xC9A227,
  0x3D7F8F,
];

/// What every actor in view looks like and is doing: the `Look` of a player,
/// from what they wear, and the `Anim` of a player or a creature, from how it
/// moved this tick and what it was told to play.
///
/// The server sends item ids and states. The client builds the parts and plays
/// the clips.
final class Actors {
  Actors._(
    this._services, {
    required Movement movement,
    required Items bags,
    required Npcs npcs,
    required ComponentType look,
    required ComponentType anim,
  }) : _movement = movement,
       _bags = bags,
       _npcs = npcs,
       _look = look,
       _anim = anim;

  /// Adds the components to [registry] and returns the feature, so a feature
  /// that swings or works can start an act.
  factory Actors.register(
    Registry registry,
    Services services, {
    required Movement movement,
    required Items bags,
    required Npcs npcs,
  }) {
    final world = services.world;
    final look = world.registerComponent(
      lookName,
      kind: ComponentKind.int32,
      arity: lookInts,
    );
    final anim = world.registerComponent(
      animName,
      kind: ComponentKind.int32,
      arity: animInts,
    );
    registry
      ..replicate(look)
      ..replicate(anim);
    final feature = Actors._(
      services,
      movement: movement,
      bags: bags,
      npcs: npcs,
      look: look,
      anim: anim,
    );
    registry
      ..addJoinHandler(feature._join)
      ..addSystem(Phase.outbound, feature._sync);
    npcs.addStandHandler(feature._stood);
    return feature;
  }

  final Services _services;
  final Movement _movement;
  final Items _bags;
  final Npcs _npcs;
  final ComponentType _look;
  final ComponentType _anim;

  /// Starts [act] on the actor [entity]. An entity that is gone is left alone.
  void play(int entity, ActorState act) {
    final bytes = _services.world.bytesOf(entity, _anim);
    if (bytes == null) return;
    writeAnim(bytes, (tryReadAnim(bytes) ?? Anim()).playing(act));
  }

  void _join(String player) {
    final entity = _movement.actorOf(player)!; // movement joined them first
    final world = _services.world
      ..add(entity, _look)
      ..add(entity, _anim);
    writeAnim(world.bytesOf(entity, _anim)!, Anim());
    writeLook(world.bytesOf(entity, _look)!, _lookOf(player, _worn(player)));
  }

  void _stood(PlacedNpc npc) {
    final world = _services.world..add(npc.entity, _anim);
    writeAnim(world.bytesOf(npc.entity, _anim)!, Anim());
  }

  /// Runs after movement and creatures have stepped, so the resting state
  /// is how each actor moved this tick.
  void _sync() {
    for (final player in _movement.players) {
      final entity = _movement.actorOf(player)!; // it is in the world
      _rest(entity, _movement.stepsLastTick(player));
      _dress(player, entity);
    }
    for (final npc in _npcs.standing) {
      _rest(npc.entity, npc.stepsLastTick);
    }
  }

  void _rest(int entity, int steps) {
    final bytes = _services.world.bytesOf(entity, _anim)!; // both joins add it
    final now = tryReadAnim(bytes) ?? Anim();
    // Nothing sets a run speed yet, so run waits for run energy.
    final base = switch (steps) {
      0 => ActorState.idle,
      1 => ActorState.walk,
      _ => ActorState.run,
    };
    if (now.base != base) writeAnim(bytes, now.withBase(base));
  }

  // shortcut: this reads every player's look every tick. Move it to an
  // equipment change hook when a tick with many players measures slow.
  void _dress(String player, int entity) {
    final bytes = _services.world.bytesOf(entity, _look)!; // _join adds it
    final now = tryReadLook(bytes);
    if (now == null) return;
    final next = now.withWorn(_worn(player));
    if (next != now) writeLook(bytes, next);
  }

  /// The item in each equipment slot of [player], 0 for an empty one.
  List<int> _worn(String player) {
    final worn = _bags.container(player, ContainerId.equipment);
    return [
      for (var slot = 0; slot < equipmentSlots.length; slot++)
        worn?.at(slot)?.item ?? 0,
    ];
  }

  // shortcut: colours come from the name, so a player looks the same each
  // login but cannot choose. A character screen saves a choice later.
  Look _lookOf(String player, List<int> worn) {
    final hash = _hash(player);
    return Look(
      skin: _skins[hash % _skins.length],
      hair: _hairs[hash ~/ 5 % _hairs.length],
      cloth: _cloths[hash ~/ 25 % _cloths.length],
      worn: worn,
    );
  }

  /// FNV-1a, because `String.hashCode` may differ between runs.
  int _hash(String text) {
    var hash = 0x811C9DC5;
    for (final unit in text.codeUnits) {
      hash = ((hash ^ unit) * 0x01000193) & 0xFFFFFFFF;
    }
    return hash;
  }
}
