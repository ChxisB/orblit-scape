import 'package:scape_core/scape_core.dart';

import 'npcs.dart';

/// The part of an actor that a box moves with. A pose turns a bone about the
/// pivot in [modelPivots].
enum ModelBone { torso, head, armL, armR, legL, legR }

/// A point or a size in tiles, with y up and the actor facing +z. The actor
/// stands with its feet at the origin.
typedef Vec3 = ({double x, double y, double z});

const Map<ModelBone, Vec3> _personPivots = {
  ModelBone.torso: (x: 0, y: 0.7, z: 0),
  ModelBone.head: (x: 0, y: 1.35, z: 0),
  ModelBone.armL: (x: -0.32, y: 1.35, z: 0),
  ModelBone.armR: (x: 0.32, y: 1.35, z: 0),
  ModelBone.legL: (x: -0.14, y: 0.7, z: 0),
  ModelBone.legR: (x: 0.14, y: 0.7, z: 0),
};

/// One box of a model: it moves with [bone], sits with its middle at [at],
/// and is [colour] as 0xRRGGBB.
final class ModelBox {
  const ModelBox({
    required this.bone,
    required this.at,
    required this.size,
    required this.colour,
  });

  final ModelBone bone;
  final Vec3 at;
  final Vec3 size;
  final int colour;

  ModelBox scaled(double k) => ModelBox(
    bone: bone,
    at: (x: at.x * k, y: at.y * k, z: at.z * k),
    size: (x: size.x * k, y: size.y * k, z: size.z * k),
    colour: colour,
  );
}

/// A shape built in code. The version in [name] goes up when the shape
/// changes, so a client that cached the old one knows to draw it again.
/// [pivots] says where each bone turns: the hip, the neck and the shoulders
/// of a person.
final class ActorModel {
  const ActorModel(this.name, this.boxes, this.pivots);

  final String name;
  final List<ModelBox> boxes;
  final Map<ModelBone, Vec3> pivots;
}

/// The models of a game: what each creature is drawn as, and what a player is
/// drawn as given their look.
final class ModelSet {
  ModelSet({required Map<int, ActorModel> npcs}) : _npcs = Map.of(npcs);

  final Map<int, ActorModel> _npcs;

  /// The model of the creature [def], or null for one with no model.
  ActorModel? npc(int def) => _npcs[def];

  /// The ids of the creatures that have a model.
  Iterable<int> get npcIds => _npcs.keys;

  /// The person [look] describes: the body in their colours, then the part
  /// each worn item shows.
  ActorModel player(Look look) => ActorModel(_personName, [
    ..._person(skin: look.skin, hair: look.hair, cloth: look.cloth),
    for (final (slot, item) in look.worn.indexed)
      if (item != 0) ..._worn(equipmentSlots[slot], item),
  ], _personPivots);
}

const String _personName = 'person.v1';

Vec3 _v(double x, double y, double z) => (x: x, y: y, z: z);

ModelBox _box(ModelBone bone, Vec3 at, Vec3 size, int colour) =>
    ModelBox(bone: bone, at: at, size: size, colour: colour);

int _darker(int colour) =>
    ((colour >> 16 & 0xFF) * 7 ~/ 10) << 16 |
    ((colour >> 8 & 0xFF) * 7 ~/ 10) << 8 |
    (colour & 0xFF) * 7 ~/ 10;

List<ModelBox> _person({
  required int skin,
  required int hair,
  required int cloth,
  double scale = 1,
}) {
  final legs = _darker(cloth);
  return [
    _box(ModelBone.legL, _v(-0.14, 0.35, 0), _v(0.2, 0.7, 0.22), legs),
    _box(ModelBone.legR, _v(0.14, 0.35, 0), _v(0.2, 0.7, 0.22), legs),
    _box(ModelBone.torso, _v(0, 1.025, 0), _v(0.5, 0.65, 0.28), cloth),
    _box(ModelBone.armL, _v(-0.32, 1.05, 0), _v(0.14, 0.6, 0.16), cloth),
    _box(ModelBone.armR, _v(0.32, 1.05, 0), _v(0.14, 0.6, 0.16), cloth),
    _box(ModelBone.head, _v(0, 1.52, 0), _v(0.3, 0.3, 0.3), skin),
    _box(ModelBone.head, _v(0, 1.69, 0), _v(0.32, 0.08, 0.32), hair),
  ].map((box) => box.scaled(scale)).toList();
}

Map<ModelBone, Vec3> _pivotsAt(double scale) => {
  for (final MapEntry(:key, :value) in _personPivots.entries)
    key: (x: value.x * scale, y: value.y * scale, z: value.z * scale),
};

ActorModel _personModel(
  String name, {
  required int skin,
  required int hair,
  required int cloth,
  double scale = 1,
  List<ModelBox> extra = const [],
}) => ActorModel(name, [
  ..._person(skin: skin, hair: hair, cloth: cloth, scale: scale),
  ...extra,
], _pivotsAt(scale));

const List<int> _itemColours = [
  0xB87333,
  0x9EA3A8,
  0xD4AF37,
  0x5B6770,
  0x7A2E2E,
  0x2E6F40,
  0x4B3F72,
  0xC8C2B0,
];

typedef _Part = ({ModelBone bone, Vec3 at, Vec3 size});

/// What a slot shows when something is worn in it. The ring shows nothing.
final Map<String, List<_Part>> _slotParts = {
  'head': [
    (bone: ModelBone.head, at: _v(0, 1.64, 0), size: _v(0.35, 0.16, 0.35)),
  ],
  'neck': [
    (bone: ModelBone.head, at: _v(0, 1.36, 0), size: _v(0.36, 0.06, 0.2)),
  ],
  'cape': [
    (bone: ModelBone.torso, at: _v(0, 1.0, -0.17), size: _v(0.46, 0.8, 0.06)),
  ],
  'weapon': [
    (bone: ModelBone.armR, at: _v(0.32, 0.55, 0.18), size: _v(0.06, 0.7, 0.06)),
  ],
  'body': [
    (bone: ModelBone.torso, at: _v(0, 1.07, 0), size: _v(0.54, 0.5, 0.32)),
  ],
  'shield': [
    (bone: ModelBone.armL, at: _v(-0.4, 1.0, 0), size: _v(0.05, 0.4, 0.34)),
  ],
  'legs': [
    (bone: ModelBone.legL, at: _v(-0.14, 0.45, 0), size: _v(0.22, 0.5, 0.24)),
    (bone: ModelBone.legR, at: _v(0.14, 0.45, 0), size: _v(0.22, 0.5, 0.24)),
  ],
  'hands': [
    (bone: ModelBone.armL, at: _v(-0.32, 0.78, 0), size: _v(0.16, 0.14, 0.18)),
    (bone: ModelBone.armR, at: _v(0.32, 0.78, 0), size: _v(0.16, 0.14, 0.18)),
  ],
  'feet': [
    (
      bone: ModelBone.legL,
      at: _v(-0.14, 0.06, 0.04),
      size: _v(0.22, 0.12, 0.3),
    ),
    (bone: ModelBone.legR, at: _v(0.14, 0.06, 0.04), size: _v(0.22, 0.12, 0.3)),
  ],
  'ammo': [
    (bone: ModelBone.torso, at: _v(-0.1, 1.1, -0.2), size: _v(0.1, 0.4, 0.1)),
  ],
};

List<ModelBox> _worn(String slot, int item) {
  final colour = _itemColours[item % _itemColours.length];
  return [
    for (final part in _slotParts[slot] ?? const <_Part>[])
      _box(part.bone, part.at, part.size, colour),
  ];
}

List<ModelBox> _chicken() => [
  _box(ModelBone.legL, _v(-0.06, 0.1, 0), _v(0.03, 0.2, 0.03), 0xE8A33D),
  _box(ModelBone.legR, _v(0.06, 0.1, 0), _v(0.03, 0.2, 0.03), 0xE8A33D),
  _box(ModelBone.torso, _v(0, 0.33, 0), _v(0.26, 0.26, 0.38), 0xF2F2EC),
  _box(ModelBone.armL, _v(-0.15, 0.36, 0), _v(0.04, 0.16, 0.26), 0xE6E6DE),
  _box(ModelBone.armR, _v(0.15, 0.36, 0), _v(0.04, 0.16, 0.26), 0xE6E6DE),
  _box(ModelBone.head, _v(0, 0.55, 0.2), _v(0.14, 0.16, 0.14), 0xF2F2EC),
  _box(ModelBone.head, _v(0, 0.53, 0.3), _v(0.05, 0.04, 0.08), 0xE8A33D),
  _box(ModelBone.head, _v(0, 0.65, 0.2), _v(0.04, 0.05, 0.1), 0xC0392B),
];

const Map<ModelBone, Vec3> _chickenPivots = {
  ModelBone.torso: (x: 0, y: 0.2, z: 0),
  ModelBone.head: (x: 0, y: 0.46, z: 0.14),
  ModelBone.armL: (x: -0.13, y: 0.44, z: 0),
  ModelBone.armR: (x: 0.13, y: 0.44, z: 0),
  ModelBone.legL: (x: -0.06, y: 0.2, z: 0),
  ModelBone.legR: (x: 0.06, y: 0.2, z: 0),
};

/// The models the game ships with. Real ones arrive through packs.
final ModelSet defaultModels = ModelSet(
  npcs: {
    NpcId.chicken: ActorModel('chicken.v1', _chicken(), _chickenPivots),
    NpcId.goblin: _personModel(
      'goblin.v1',
      skin: 0x5E8C3A,
      hair: 0x2B3A1A,
      cloth: 0x7A5230,
      scale: 0.8,
    ),
    NpcId.guard: _personModel(
      'guard.v1',
      skin: 0xD9A877,
      hair: 0x4A3B2A,
      cloth: 0x3E5C8A,
      extra: [..._worn('head', 1), ..._worn('weapon', 1)],
    ),
    NpcId.shopkeeper: _personModel(
      'shopkeeper.v1',
      skin: 0xC68E5F,
      hair: 0x8A8A8A,
      cloth: 0x8A3E3E,
    ),
    NpcId.lamplighter: _personModel(
      'lamplighter.v1',
      skin: 0xE0B48A,
      hair: 0x5A4632,
      cloth: 0xB89B3E,
    ),
  },
);
