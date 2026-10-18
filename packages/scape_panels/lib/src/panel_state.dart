import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';

/// The tabs down the side of the screen.
enum SideTab { inventory, equipment, skills, quests, friends, settings }

const int maxVolume = 10;
const int minViewDistance = 4;
const int maxViewDistance = 24;

/// One slot of one container.
typedef Slot = ({ContainerId container, int slot});

/// What the panels show that the server does not decide: the open tab, the
/// stack picked up to move, the menu and the two settings.
final class PanelState {
  PanelState({
    this.tab = SideTab.inventory,
    this.chat = ChatTab.game,
    this.picked,
    List<MenuEntry> menu = const [],
    this.hover = '',
    this.volume = 5,
    this.viewDistance = 12,
  }) : menu = List.unmodifiable(menu) {
    if (volume < 0 || volume > maxVolume) {
      throw ArgumentError.value(volume, 'volume', 'is outside 0 to $maxVolume');
    }
    if (viewDistance < minViewDistance || viewDistance > maxViewDistance) {
      throw ArgumentError.value(
        viewDistance,
        'viewDistance',
        'is out of range',
      );
    }
  }

  final SideTab tab;
  final ChatTab chat;

  /// The stack the player tapped first, waiting for the slot to move it to.
  final Slot? picked;

  /// The right-click menu, or empty when none is open.
  final List<MenuEntry> menu;

  /// What the pointer is over, as text.
  final String hover;

  final int volume;
  final int viewDistance;

  /// A copy with changes. [picked] is a function so that null can be asked
  /// for: `picked: () => null` puts the stack down.
  PanelState copyWith({
    SideTab? tab,
    ChatTab? chat,
    Slot? Function()? picked,
    List<MenuEntry>? menu,
    String? hover,
    int? volume,
    int? viewDistance,
  }) => PanelState(
    tab: tab ?? this.tab,
    chat: chat ?? this.chat,
    picked: picked == null ? this.picked : picked(),
    menu: menu ?? this.menu,
    hover: hover ?? this.hover,
    volume: volume ?? this.volume,
    viewDistance: viewDistance ?? this.viewDistance,
  );
}
