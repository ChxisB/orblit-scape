import 'package:scape_core/scape_core.dart';

import 'session.dart';

/// One line of the right-click menu. [run] sends what the line does.
final class MenuEntry {
  const MenuEntry(this.label, this.run);

  final String label;
  final void Function() run;
}

/// What a click on something in view offers, built from the content the game
/// was made with. The first entry is the click that needs no menu, and its
/// label is the hover text.
///
/// The menu of a tile, Walk here and Cancel belong to the panel that draws it.
final class Menus {
  Menus({
    required this.session,
    required this.npcs,
    required this.objects,
    required this.items,
  });

  final GameSession session;
  final NpcSet npcs;
  final ObjectSet objects;
  final ItemSet items;

  /// Attack, Trade and Talk, for the ones this creature has.
  List<MenuEntry> onNpc(NpcSprite sprite) {
    final def = npcs.find(sprite.def);
    if (def == null) return const [];
    final id = sprite.networkId;
    return [
      if (def.fight != null)
        MenuEntry('Attack ${def.name}', () => session.attack(id)),
      if (def.shop != null)
        MenuEntry('Trade ${def.name}', () => session.trade(id)),
      if (def.dialogue != null)
        MenuEntry('Talk to ${def.name}', () => session.talk(id)),
    ];
  }

  /// The options this object offers in the state it is in now.
  List<MenuEntry> onObject(ObjectSprite sprite) {
    final def = objects.find(sprite.def);
    if (def == null) return const [];
    return [
      for (final (index, option) in def.options.indexed)
        if (option.offeredIn(sprite.state))
          MenuEntry(
            '${option.label} ${def.name}',
            () => session.useObject(sprite.networkId, index),
          ),
    ];
  }

  List<MenuEntry> onGround(GroundSprite sprite) {
    final def = items.find(sprite.item);
    if (def == null) return const [];
    return [
      MenuEntry('Take ${def.name}', () => session.takeItem(sprite.networkId)),
    ];
  }

  List<MenuEntry> onPlayer(Actor actor) => [
    if (!actor.mine)
      MenuEntry('Trade player', () => session.requestTrade(actor.networkId)),
  ];
}
