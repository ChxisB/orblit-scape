import 'package:orblit_ui/orblit_ui.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';

import 'panel_state.dart';

const int _columns = 4;
const int _chatLines = 6;

/// How many tiles the minimap shows each way, and how many pixels it is wide.
const int _mapRadius = 16;
const double _mapSize = 100;
const double _dot = 4;

/// The corner a dot of [_dot] pixels is drawn from to sit centred on [at] and
/// stay inside the map.
double _corner(double at) => (at - _dot / 2).clamp(0, _mapSize - _dot);

UiNode _text(String text, [String classes = '']) =>
    UiNode(type: 'text', text: text, classes: classes);

UiNode _button(String label, String handler, [String classes = '']) => UiNode(
  type: 'button',
  text: label,
  classes: 'px-2 py-1 rounded bg-slate-700 text-center $classes',
  props: {'onTap': handler},
);

UiNode _column(List<UiNode> children, [String classes = '']) =>
    UiNode(type: 'column', classes: 'gap-1 $classes', children: children);

UiNode _row(List<UiNode> children, [String classes = '']) =>
    UiNode(type: 'row', classes: 'gap-1 $classes', children: children);

/// The panels as one `orblit_ui` tree. It reads the session and the state and
/// changes neither, so the same inputs give the same tree.
final class Panels {
  Panels(this.session, this.items, this.skills);

  final GameSession session;
  final ItemSet items;
  final SkillSet skills;

  UiNode build(PanelState state) => UiNode(
    type: 'box',
    classes: 'col md:row w-full h-full bg-slate-900 text-slate-100 text-sm',
    children: [_world(state), _side(state)],
  );

  UiNode _world(PanelState state) => UiNode(
    type: 'stack',
    classes: 'grow p-2',
    children: [
      _text(state.hover, 'left-0 top-0'),
      _minimap(),
      if (state.menu.isNotEmpty) _menu(state.menu),
    ],
  );

  UiNode _menu(List<MenuEntry> entries) => UiNode(
    type: 'column',
    classes: 'left-[40] top-[40] gap-1 p-1 rounded bg-slate-800',
    children: [
      for (final (index, entry) in entries.indexed)
        _button(entry.label, 'menu:$index'),
      _button('Cancel', 'menu-close'),
    ],
  );

  UiNode _minimap() {
    const centre = _mapSize / 2;
    const scale = _mapSize / (_mapRadius * 2);
    UiNode dot(double x, double y, String colour) => UiNode(
      type: 'box',
      classes:
          'left-[${_corner(x)}] top-[${_corner(y)}] w-[$_dot] h-[$_dot] '
          'bg-[$colour]',
    );
    return UiNode(
      type: 'stack',
      classes: 'right-[8] top-[8] w-[$_mapSize] h-[$_mapSize] bg-slate-800',
      children: [
        for (final (:dx, :dy, :kind) in minimapDots(
          session,
          radius: _mapRadius,
        ))
          dot(
            centre + dx * scale,
            centre - dy * scale,
            kind == DotKind.npc ? '#facc15' : '#38bdf8',
          ),
        dot(centre, centre, '#ffffff'),
      ],
    );
  }

  UiNode _side(PanelState state) => UiNode(
    type: 'column',
    classes: 'gap-1 p-2 w-full md:w-[320] bg-slate-800',
    children: [
      _tabs(state),
      _window() ?? _body(state),
      if (_takesInventoryTaps) _grid(ContainerId.inventory, state),
      _chat(state),
    ],
  );

  /// A bank and a trade are fed by taps on the inventory, so it stays on
  /// screen under their window.
  bool get _takesInventoryTaps =>
      session.bankOpen || session.playerTrade != null;

  UiNode _tabs(PanelState state) => _row([
    for (final tab in SideTab.values)
      _button(
        tab.name.substring(0, 3),
        'tab:${tab.name}',
        'grow ${tab == state.tab ? 'bg-slate-500' : ''}',
      ),
  ]);

  UiNode _body(PanelState state) => switch (state.tab) {
    SideTab.inventory => _grid(ContainerId.inventory, state),
    SideTab.equipment => _gear(state),
    SideTab.skills => _skills(),
    SideTab.quests => _quests(),
    SideTab.friends => _friends(),
    SideTab.settings => _settings(state),
  };

  UiNode _grid(ContainerId container, PanelState state) => _column([
    for (var from = 0; from < containerSize(container); from += _columns)
      _row([
        for (var slot = from; slot < from + _columns; slot++)
          _cell(container, slot, state),
      ]),
  ]);

  UiNode _cell(ContainerId container, int slot, PanelState state) {
    final stack = session.itemAt(container, slot);
    final picked = state.picked == (container: container, slot: slot);
    return UiNode(
      type: 'button',
      text: stack == null ? '' : _label(stack),
      classes:
          'grow h-10 rounded text-xs ${picked ? 'bg-amber-600' : 'bg-slate-700'}',
      props: {'onTap': 'slot:${container.name}:$slot'},
    );
  }

  String _label(ItemStack stack) {
    final name = _name(stack.item);
    return stack.count > 1 ? '$name x${stack.count}' : name;
  }

  /// A client with older content can be sent an item it has no name for.
  String _name(int item) => items.find(item)?.name ?? 'Item $item';

  UiNode _gear(PanelState state) => _column([
    for (final (slot, name) in equipmentSlots.indexed)
      _row([
        _text(name, 'w-16 text-xs'),
        _cell(ContainerId.equipment, slot, state),
      ]),
  ]);

  UiNode _skills() => _column([
    for (final skill in skills.skills)
      _row([_text(skill.name, 'grow'), _text('${session.levelOf(skill.id)}')]),
  ]);

  UiNode _quests() {
    final journal = session.journal;
    if (journal.isEmpty) return _text('No quests begun.');
    return _column([
      for (final (:title, :text, :done) in journal)
        _column([
          _text(done ? '$title (done)' : title, 'font-bold'),
          _text(text, 'text-xs'),
        ]),
    ]);
  }

  UiNode _friends() => _column([
    for (final (:name, :online) in session.friends)
      _text(online ? '$name (online)' : name),
    if (session.friends.isEmpty) _text('No friends added.'),
    for (final name in session.ignored) _text('$name (ignored)', 'text-xs'),
  ]);

  UiNode _settings(PanelState state) => _column([
    _stepper('Volume', 'volume', state.volume),
    _stepper('View distance', 'view', state.viewDistance),
  ]);

  UiNode _stepper(String label, String handler, int value) => _row([
    _text(label, 'grow'),
    _button('-', '$handler:-1'),
    _text('$value', 'w-8 text-center'),
    _button('+', '$handler:1'),
  ]);

  UiNode _chat(PanelState state) {
    final lines = session.chat(state.chat);
    return _column([
      _row([
        for (final tab in ChatTab.values)
          _button(
            tab.name,
            'chat:${tab.name}',
            'grow text-xs ${tab == state.chat ? 'bg-slate-500' : ''}',
          ),
      ]),
      for (final line in lines.skip(
        (lines.length - _chatLines).clamp(0, lines.length),
      ))
        _text(line, 'text-xs'),
    ], 'p-1 rounded bg-slate-900');
  }

  /// The window the server has open, or null. A trade, a shop, a bank and a
  /// conversation are never open together.
  UiNode? _window() {
    final trade = session.playerTrade;
    if (trade != null) return _trade(trade);
    final shop = session.shop;
    if (shop != null) return _shop(shop);
    if (session.bankOpen) return _bank();
    final line = session.line;
    if (line != null) return _line(line);
    if (session.choices.isNotEmpty) return _choices();
    return null;
  }

  UiNode _trade(TradeView trade) => _column([
    _text('Trading with ${trade.partner}', 'font-bold'),
    _text('You offer: ${_stacks(trade.mine)}', 'text-xs'),
    _text('They offer: ${_stacks(trade.theirs)}', 'text-xs'),
    _row([
      _button(trade.confirming ? 'Confirm' : 'Accept', 'accept', 'grow'),
      _button('Decline', 'decline', 'grow'),
    ]),
  ]);

  String _stacks(List<ItemStack> stacks) =>
      stacks.isEmpty ? 'nothing' : stacks.map(_label).join(', ');

  UiNode _shop(ShopView shop) => _column([
    _text('Shop', 'font-bold'),
    for (final (:item, :count) in shop.lines)
      _button('${_name(item)} ($count left)', 'buy:$item', 'text-left'),
  ]);

  // shortcut: the first bank tab only, add a tab strip when a bank needs it.
  UiNode _bank() => _column([
    _text('Bank', 'font-bold'),
    UiNode(
      type: 'column',
      classes: 'gap-1 h-[120]',
      css: 'overflow-y: auto',
      children: [
        for (var slot = 0; slot < bankTabSize; slot++)
          if (session.itemAt(ContainerId.bank, slot) case final stack?)
            _button(_label(stack), 'withdraw:$slot', 'text-left'),
      ],
    ),
  ]);

  UiNode _line(ConversationLine line) => _column([
    _text(line.speaker, 'font-bold'),
    _text(line.text),
    _button('Continue', 'continue'),
  ]);

  UiNode _choices() => _column([
    for (final (index, choice) in session.choices.indexed)
      _button(choice, 'choose:$index', 'text-left'),
  ]);
}
