import 'dart:convert';
import 'dart:typed_data';

import 'container.dart';
import 'items.dart';
import 'messages.dart';
import 'tile_coord.dart';

/// The version `encode` writes. A reader refuses a later one.
const int playerStateVersion = 2;

/// The most experience a skill may hold in a save. A progress table keeps it
/// in 32 bits.
const int maxSavedXp = 0xFFFFFFFF;

/// Everything a server keeps about a player between visits: where they
/// stand, their experience, what they carry, wear and bank, and their lists.
///
/// It is a value. [encode] and [PlayerState.decode] are the only way it
/// becomes bytes, and the bytes are JSON, so an operator can read a save and
/// fix one. Skills are named by id and items by number, so a game that adds
/// a skill leaves old saves valid.
final class PlayerState {
  PlayerState({
    required this.tile,
    Map<String, int> skills = const {},
    Map<String, int> quests = const {},
    Map<ContainerId, List<ItemStack?>> containers = const {},
    Iterable<String> friends = const [],
    Iterable<String> ignored = const [],
  }) : skills = Map.unmodifiable(skills),
       quests = Map.unmodifiable(quests),
       containers = Map.unmodifiable({
         for (final MapEntry(:key, :value) in containers.entries)
           key: List<ItemStack?>.unmodifiable(value),
       }),
       friends = List.unmodifiable(friends),
       ignored = List.unmodifiable(ignored) {
    _checkWholes('skills', this.skills);
    _checkWholes('quests', this.quests);
    _checkContainers(this.containers);
    _checkNames('friends', this.friends);
    _checkNames('ignored', this.ignored);
  }

  /// Reads what [encode] wrote. Throws [FormatException] for bytes that are
  /// not a player state, or that a later version wrote.
  factory PlayerState.decode(Uint8List bytes) {
    try {
      return _read(jsonDecode(utf8.decode(bytes)));
    } on ArgumentError catch (e) {
      // The constructors check the values, so a bad value is a bad file.
      throw FormatException('Not a player state: ${e.message}');
    }
  }

  final TileCoord tile;

  /// Experience by skill id. A skill that is absent has none.
  final Map<String, int> skills;

  /// The stage of each quest by quest key. A quest that is absent is not begun.
  final Map<String, int> quests;

  /// The slots of each container, empty ones null. A container that is
  /// absent is empty.
  final Map<ContainerId, List<ItemStack?>> containers;

  /// Player keys, lower case.
  final List<String> friends;
  final List<String> ignored;

  /// The same bytes for the same state, whatever order it was built in.
  Uint8List encode() => Uint8List.fromList(
    utf8.encode(
      jsonEncode({
        'version': playerStateVersion,
        'tile': [tile.x, tile.y, tile.plane],
        'skills': {
          for (final id in skills.keys.toList()..sort()) id: skills[id],
        },
        'quests': {
          for (final key in quests.keys.toList()..sort()) key: quests[key],
        },
        'containers': {
          for (final id in ContainerId.values)
            if (containers[id] case final slots?) id.name: _sparse(slots),
        },
        'friends': friends,
        'ignored': ignored,
      }),
    ),
  );
}

List<List<int>> _sparse(List<ItemStack?> slots) => [
  for (final (slot, stack) in slots.indexed)
    if (stack != null) [slot, stack.item, stack.count],
];

void _checkWholes(String label, Map<String, int> wholes) {
  for (final MapEntry(:key, :value) in wholes.entries) {
    if (key.isEmpty) throw ArgumentError.value(key, label, 'has no id');
    if (value < 0 || value > maxSavedXp) {
      throw ArgumentError.value(value, key, 'is outside 0 to $maxSavedXp');
    }
  }
}

// shortcut: a container whose size changes makes every older save fail to
// decode, since the size is part of the check. Bump playerStateVersion and read
// the old shape when a game first resizes one.
void _checkContainers(Map<ContainerId, List<ItemStack?>> containers) {
  for (final MapEntry(:key, :value) in containers.entries) {
    if (value.length != containerSize(key)) {
      throw ArgumentError.value(
        value.length,
        key.name,
        'is not ${containerSize(key)} slots',
      );
    }
  }
}

void _checkNames(String label, List<String> names) {
  if (names.length > maxListEntries) {
    throw ArgumentError.value(names.length, label, 'is over the list limit');
  }
  for (final name in names) {
    if (!isValidName(name) || nameKey(name) != name) {
      throw ArgumentError.value(name, label, 'is not a player key');
    }
  }
}

FormatException _bad(String what) =>
    FormatException('Not a player state: $what');

PlayerState _read(Object? json) {
  final map = _object(json, 'the state');
  final version = map['version'];
  if (version is! int || version < 1) throw _bad('no version');
  if (version > playerStateVersion) {
    throw _bad('version $version is from a later server');
  }
  if (map['tile'] case [final int x, final int y, final int plane]) {
    return PlayerState(
      tile: TileCoord(x, y, plane),
      skills: _wholes(map['skills'], 'skill'),
      quests: _wholes(map['quests'], 'quest'),
      containers: _containers(map['containers']),
      friends: _names(map['friends'], 'friends'),
      ignored: _names(map['ignored'], 'ignored'),
    );
  }
  throw _bad('tile is not [x, y, plane]');
}

Map<String, Object?> _object(Object? json, String what) =>
    json is Map<String, Object?> ? json : throw _bad('$what is not an object');

List<Object?> _list(Object? json, String what) =>
    json is List<Object?> ? json : throw _bad('$what is not a list');

Map<String, int> _wholes(Object? json, String what) {
  if (json == null) return const {};
  return {
    for (final MapEntry(:key, :value) in _object(json, '${what}s').entries)
      key: value is int ? value : throw _bad('$what $key is not a number'),
  };
}

Map<ContainerId, List<ItemStack?>> _containers(Object? json) {
  if (json == null) return const {};
  final names = ContainerId.values.asNameMap();
  final read = <ContainerId, List<ItemStack?>>{};
  for (final MapEntry(:key, :value) in _object(json, 'containers').entries) {
    final id = names[key] ?? (throw _bad('no container called $key'));
    read[id] = _slots(id, value);
  }
  return read;
}

List<ItemStack?> _slots(ContainerId id, Object? json) {
  final slots = List<ItemStack?>.filled(containerSize(id), null);
  for (final entry in _list(json, id.name)) {
    if (entry case [
      final int slot,
      final int item,
      final int count,
    ] when slot >= 0 && slot < slots.length && slots[slot] == null) {
      slots[slot] = ItemStack(item, count);
    } else {
      throw _bad('${id.name} has a bad slot, $entry');
    }
  }
  return slots;
}

List<String> _names(Object? json, String what) {
  if (json == null) return const [];
  return [
    for (final name in _list(json, what))
      name is String ? name : throw _bad('$what holds a non-name'),
  ];
}
