import 'dart:math';

import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

List<Object> _vec(Vec3 v) => [number(v.x), number(v.y), number(v.z)];

_Json? _model(ActorModel? model) => model == null
    ? null
    : {
        'name': model.name,
        'boxes': [
          for (final b in model.boxes)
            [b.bone.name, _vec(b.at), _vec(b.size), b.colour],
        ],
        'pivots': {
          for (final MapEntry(:key, :value) in model.pivots.entries)
            key.name: _vec(value),
        },
      };

_Json _act(ActorState state) => {
  'op': 'act',
  'state': state.name,
  'expect': switch (actSeconds(state)) {
    final seconds? => number(seconds),
    null => null,
  },
};

_Json _pose(ActorState state, double seconds) {
  final p = poseOf(state, seconds);
  return {
    'op': 'pose',
    'state': state.name,
    'seconds': seconds,
    'expect': [
      for (final v in [p.legL, p.legR, p.armL, p.armR, p.lean, p.lie])
        number(v),
    ],
  };
}

// Each act at the points its tests look at, and a long rest at a large time.
List<double> _timesOf(ActorState state) => [
  for (var i = -2; i <= 40; i++) i * 0.05,
  1000.37,
  if (actSeconds(state) case final length?) ...[
    length / 4,
    length / 2,
    length,
    length * 5,
  ],
];

_Json _npc(int id) => {
  'op': 'npc',
  'id': id,
  'expect': _model(defaultModels.npc(id)),
};

_Json _player(Look look) => {
  'op': 'player',
  'skin': look.skin,
  'hair': look.hair,
  'cloth': look.cloth,
  'worn': look.worn,
  'expect': _model(defaultModels.player(look)),
};

Look _dressed(Map<String, int> worn) => Look(
  skin: 0xE0AC69,
  hair: 0x3B2F2F,
  cloth: 0x2F6FDB,
  worn: [for (final slot in equipmentSlots) worn[slot] ?? 0],
);

Look _randomLook(Random random) => Look(
  skin: random.nextInt(0x1000000),
  hair: random.nextInt(0x1000000),
  cloth: random.nextInt(0x1000000),
  worn: [
    for (final _ in equipmentSlots)
      random.nextBool() ? 0 : random.nextInt(maxItemId + 1),
  ],
);

List<Look> _looks() {
  final random = Random(18);
  final none = [for (final _ in equipmentSlots) 0];
  return [
    _dressed({}),
    for (final slot in equipmentSlots)
      for (final item in [1, 2, 7, 8, 9, maxItemId]) _dressed({slot: item}),
    Look(skin: 0, hair: 0, cloth: 0, worn: none),
    Look(skin: 0xFFFFFF, hair: 0xFFFFFF, cloth: 0xFFFFFF, worn: none),
    for (var i = 0; i < 20; i++) _randomLook(random),
  ];
}

void main(List<String> args) {
  writeFixture(
    root: args.single,
    package: 'scape_content',
    name: 'look',
    cases: [
      for (final state in ActorState.values) _act(state),
      for (final state in ActorState.values)
        for (final seconds in _timesOf(state)) _pose(state, seconds),
      {'op': 'npcIds', 'expect': defaultModels.npcIds.toList()},
      for (final id in [...defaultModels.npcIds, 0, 9999]) _npc(id),
      for (final look in _looks()) _player(look),
    ],
  );
}
