import 'dart:typed_data';

import 'items.dart';

/// What an actor is doing. The first three are how it stands between other
/// things, and the server keeps them true. The rest play once, when an event
/// starts them.
enum ActorState {
  idle,
  walk,
  run,
  attack,
  hurt,
  death,
  gather;

  /// Whether this state plays once, started by an event.
  bool get isAct => index > run.index;
}

/// The name of the replicated component holding what an actor is doing.
const String animName = 'Anim';

/// Ints in that component: the resting state, the act or -1, then the count
/// of acts so far.
const int animInts = 3;

/// The count of acts wraps back to zero after this.
const int maxAnimSeq = 0xFFFF;

/// What an actor is doing: how it rests, and the act it last played. The
/// count of acts goes up with each one, so the same act twice in a row is
/// still a change a client can see.
final class Anim {
  Anim({this.base = ActorState.idle, this.act, this.seq = 0}) {
    if (base.isAct) {
      throw ArgumentError.value(base, 'base', 'is not a resting state');
    }
    if (act case final played? when !played.isAct) {
      throw ArgumentError.value(played, 'act', 'is a resting state');
    }
    RangeError.checkValueInInterval(seq, 0, maxAnimSeq, 'seq');
  }

  final ActorState base;
  final ActorState? act;
  final int seq;

  Anim withBase(ActorState next) => Anim(base: next, act: act, seq: seq);

  /// The same actor playing [next] now.
  Anim playing(ActorState next) =>
      Anim(base: base, act: next, seq: seq >= maxAnimSeq ? 0 : seq + 1);

  @override
  bool operator ==(Object other) =>
      other is Anim &&
      other.base == base &&
      other.act == act &&
      other.seq == seq;

  @override
  int get hashCode => Object.hash(base, act, seq);
}

/// Writes [anim] into the component's bytes.
void writeAnim(Uint8List bytes, Anim anim) {
  ByteData.sublistView(bytes)
    ..setInt32(0, anim.base.index, Endian.little)
    ..setInt32(4, anim.act?.index ?? -1, Endian.little)
    ..setInt32(8, anim.seq, Endian.little);
}

/// Reads the component's bytes, or null for bytes that name no state.
Anim? tryReadAnim(Uint8List bytes) {
  final data = ByteData.sublistView(bytes);
  final base = _stateAt(data.getInt32(0, Endian.little));
  final actIndex = data.getInt32(4, Endian.little);
  final act = actIndex == -1 ? null : _stateAt(actIndex);
  if (base == null || (actIndex != -1 && act == null)) return null;
  try {
    return Anim(base: base, act: act, seq: data.getInt32(8, Endian.little));
  } on ArgumentError {
    return null;
  }
}

ActorState? _stateAt(int index) =>
    index >= 0 && index < ActorState.values.length
    ? ActorState.values[index]
    : null;

/// The name of the replicated component holding how a player looks.
const String lookName = 'Look';

/// The colours of a look: skin, hair and cloth.
const int lookColours = 3;

/// Ints in that component: the colours, then the item worn in each of the
/// [equipmentSlots], 0 for a slot that is empty.
final int lookInts = lookColours + equipmentSlots.length;

const int _maxColour = 0xFFFFFF;

/// How a player looks: three colours and what each equipment slot shows. The
/// server sends item ids and the client builds the parts, so a new part is
/// content and not a new message.
final class Look {
  Look({
    required this.skin,
    required this.hair,
    required this.cloth,
    required List<int> worn,
  }) : worn = List.unmodifiable(worn) {
    for (final colour in [skin, hair, cloth]) {
      RangeError.checkValueInInterval(colour, 0, _maxColour, 'colour');
    }
    if (worn.length != equipmentSlots.length) {
      throw ArgumentError.value(worn, 'worn', 'is not one item per slot');
    }
    for (final item in worn) {
      RangeError.checkValueInInterval(item, 0, maxItemId, 'worn item');
    }
  }

  /// Colours as 0xRRGGBB.
  final int skin;
  final int hair;
  final int cloth;

  /// The item id worn in each slot, in the order of [equipmentSlots].
  final List<int> worn;

  Look withWorn(List<int> next) =>
      Look(skin: skin, hair: hair, cloth: cloth, worn: next);

  @override
  bool operator ==(Object other) =>
      other is Look &&
      other.skin == skin &&
      other.hair == hair &&
      other.cloth == cloth &&
      _same(other.worn, worn);

  @override
  int get hashCode => Object.hash(skin, hair, cloth, Object.hashAll(worn));

  static bool _same(List<int> a, List<int> b) {
    for (var i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }
}

/// Writes [look] into the component's bytes.
void writeLook(Uint8List bytes, Look look) {
  final data = ByteData.sublistView(bytes)
    ..setInt32(0, look.skin, Endian.little)
    ..setInt32(4, look.hair, Endian.little)
    ..setInt32(8, look.cloth, Endian.little);
  for (final (slot, item) in look.worn.indexed) {
    data.setInt32((lookColours + slot) * 4, item, Endian.little);
  }
}

/// Reads the component's bytes, or null for bytes that are not a look.
Look? tryReadLook(Uint8List bytes) {
  final data = ByteData.sublistView(bytes);
  final ints = [
    for (var i = 0; i < lookInts; i++) data.getInt32(i * 4, Endian.little),
  ];
  try {
    return Look(
      skin: ints[0],
      hair: ints[1],
      cloth: ints[2],
      worn: ints.sublist(lookColours),
    );
  } on ArgumentError {
    return null;
  }
}
