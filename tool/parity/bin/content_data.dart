import 'dart:convert';

import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_parity_tool/fixture.dart';

typedef _Json = Map<String, Object?>;

List<int> _stack(ItemStack stack) => [stack.item, stack.count];

_Json _sets() => {
  'op': 'sets',
  'expect': {
    'curve': [defaultSkills.curve.quadratic, defaultSkills.curve.cubic],
    'skills': [
      for (final s in defaultSkills.skills) [s.id, s.name, s.levelCap],
    ],
    'items': [for (final i in defaultItems.items) i.id],
    'objects': [for (final o in defaultObjects.objects) o.id],
    'nodes': [for (final n in defaultNodes.nodes) n.object],
    'recipes': [for (final r in defaultRecipes.recipes) r.name],
    'npcs': [for (final n in defaultNpcs.npcs) n.id],
    'spells': {
      'skill': defaultSpells.skill,
      'ids': [for (final s in defaultSpells.spells) s.id],
    },
    'prayers': {
      'skill': defaultPrayers.skill,
      'drainPerPoint': defaultPrayers.drainPerPoint,
      'ids': [for (final p in defaultPrayers.prayers) p.id],
    },
    'combat': [defaultCombat.fightingSkill, defaultCombat.healthSkill],
    'shops': [
      defaultShops.coins,
      defaultShops.buyPercent,
      defaultShops.sellPercent,
    ],
  },
};

_Json? _bonus(CombatBonus? c) => c == null
    ? null
    : {
        'attack': c.attack,
        'strength': c.strength,
        'defence': c.defence,
        'speed': c.speed,
        'style': c.style.name,
        'range': c.range,
        'ammo': c.ammo,
      };

_Json _item(ItemDef i) => {
  'op': 'item',
  'id': i.id,
  'expect': {
    'name': i.name,
    'stackable': i.stackable,
    'value': i.value,
    'equipSlot': i.equipSlot,
    'needs': i.needs == null ? null : [i.needs!.skill, i.needs!.level],
    'combat': _bonus(i.combat),
  },
};

_Json _object(ObjectDef o) => {
  'op': 'object',
  'id': o.id,
  'expect': {
    'name': o.name,
    'width': o.width,
    'height': o.height,
    'solid': o.solid,
    'options': [
      for (final option in o.options)
        [option.label, option.action, option.whenState],
    ],
  },
};

_Json _node(NodeDef n) => {
  'op': 'node',
  'object': n.object,
  'expect': {
    'skill': n.skill,
    'level': n.level,
    'tool': n.tool,
    'xp': n.xp,
    'product': n.product,
    'chanceLow': number(n.chanceLow),
    'chanceHigh': number(n.chanceHigh),
    'fullLevel': n.fullLevel,
    'depleteChance': number(n.depleteChance),
    'respawnTicks': n.respawnTicks,
  },
};

_Json _recipe(Recipe r) => {
  'op': 'recipe',
  'name': r.name,
  'expect': {
    'skill': r.skill,
    'level': r.level,
    'xp': r.xp,
    'start': switch (r.start) {
      OnItem(:final item, :final target) => {'onItem': [item, target]},
      OnObject(:final item, :final object) => {'onObject': [item, object]},
    },
    'inputs': [for (final s in r.inputs) _stack(s)],
    'product': switch (r.product) {
      Gives(:final stack) => {'gives': _stack(stack)},
      Lights(:final object, :final ticks) => {'lights': [object, ticks]},
    },
    'tool': r.tool,
    'ticks': r.ticks,
    'failure': switch (r.failure) {
      null => null,
      final f => [number(f.chance), f.safeLevel, f.item],
    },
  },
};

_Json _table(LootTable t) => {
  'always': [for (final s in t.always) _stack(s)],
  'entries': [
    for (final e in t.entries)
      switch (e) {
        LootItem() => [e.weight, e.item, e.min, e.max],
        LootSub() => {'weight': e.weight, 'table': _table(e.table)},
      },
  ],
  'nothing': t.nothing,
  'rare': t.rare == null ? null : _table(t.rare!),
  'rareOneIn': t.rareOneIn,
};

_Json _npc(NpcDef n) => {
  'op': 'npc',
  'id': n.id,
  'expect': {
    'name': n.name,
    'behaviour': switch (n.behaviour) {
      Idle() => 'idle',
      Wander() => 'wander',
      Aggressive(:final sight, :final leash) => [sight, leash],
    },
    'fight': switch (n.fight) {
      null => null,
      final f => {
        'health': f.health,
        'attack': f.attack,
        'strength': f.strength,
        'defence': f.defence,
        'speed': f.speed,
        'respawnTicks': f.respawnTicks,
        'style': f.style.name,
      },
    },
    'loot': n.loot == null ? null : _table(n.loot!),
    'shop': n.shop == null
        ? null
        : [
            for (final l in n.shop!.lines)
              [l.item, l.cap, l.start, l.restockTicks],
          ],
    'dialogue': n.dialogue,
  },
};

_Json _spell(SpellDef s) => {
  'op': 'spell',
  'id': s.id,
  'expect': {
    'name': s.name,
    'level': s.level,
    'effect': switch (s.effect) {
      SpellDamage(:final most) => {'damage': most},
      SpellStun(:final ticks) => {'stun': ticks},
      SpellTeleport(:final to) => {'teleport': [to.x, to.y, to.plane]},
    },
    'cost': [for (final c in s.cost) _stack(c)],
    'range': s.range,
    'speed': s.speed,
    'xp': s.xp,
  },
};

_Json _prayer(PrayerDef p) => {
  'op': 'prayer',
  'id': p.id,
  'expect': [p.name, p.level, p.stat.name, p.percent, p.drain],
};

/// Quests and dialogues travel as their decoded JSON, so key order and
/// spacing do not count.
_Json _story() => {
  'op': 'story',
  'expect': {
    'quests': [
      for (final q in defaultStory.book.quests) tree(jsonDecode(q.encode())),
    ],
    'dialogues': [
      for (final d in defaultStory.dialogues) tree(jsonDecode(d.encode())),
    ],
    'steps': {
      for (final MapEntry(:key, :value) in defaultStory.steps.entries)
        key: [value.quest, value.stage],
    },
    'rewards': {
      for (final MapEntry(:key, :value) in defaultStory.rewards.entries)
        key: {
          'xp': value.xp,
          'items': [for (final s in value.items) _stack(s)],
        },
    },
  },
};

void main(List<String> args) {
  writeFixture(
    root: args.single,
    package: 'scape_content',
    name: 'data',
    cases: [
      _sets(),
      ...defaultItems.items.map(_item),
      ...defaultObjects.objects.map(_object),
      ...defaultNodes.nodes.map(_node),
      ...defaultRecipes.recipes.map(_recipe),
      ...defaultNpcs.npcs.map(_npc),
      ...defaultSpells.spells.map(_spell),
      ...defaultPrayers.prayers.map(_prayer),
      _story(),
    ],
  );
}
