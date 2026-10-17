import 'package:scape_core/scape_core.dart';

const int coins = 1;
const int logs = 2;
const int sword = 3;
const int helmet = 4;
const int arrows = 5;

final SkillSet testSkills = SkillSet(
  curve: XpCurve(quadratic: 30, cubic: 4),
  skills: [SkillDef(id: 'fighting', name: 'Fighting', levelCap: 50)],
);

ItemSet testItems() => ItemSet(
  skills: testSkills,
  items: [
    ItemDef(id: coins, name: 'Coins', stackable: true, value: 1),
    ItemDef(id: logs, name: 'Logs', value: 4),
    ItemDef(
      id: sword,
      name: 'Sword',
      equipSlot: 'weapon',
      needs: (skill: 'fighting', level: 5),
    ),
    ItemDef(id: helmet, name: 'Helmet', equipSlot: 'head'),
    ItemDef(id: arrows, name: 'Arrows', stackable: true, equipSlot: 'weapon'),
  ],
);
