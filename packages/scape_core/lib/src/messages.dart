import 'dart:convert';
import 'dart:typed_data';

import 'combat.dart';
import 'container.dart';
import 'items.dart';
import 'magic.dart';
import 'prayer.dart';
import 'shops.dart';
import 'tile_coord.dart';

/// Letters and digits first, then up to eleven more with spaces, underscores
/// and hyphens inside, and no space at the end.
final RegExp _name = RegExp(
  r'^[A-Za-z0-9]([A-Za-z0-9 _-]{0,10}[A-Za-z0-9_-])?$',
);

const int maxNameChars = 12;

/// The most a line of chat or a notice may take once encoded as UTF-8.
const int maxTextBytes = 240;

/// The most names a friends list or an ignore list may hold.
const int maxListEntries = 50;

/// The most bytes a password may take as UTF-8.
const int maxPasswordBytes = 64;

/// The fewest bytes a new account's password may take as UTF-8.
const int minPasswordBytes = 8;

/// The most a [PrivateState] delta may hold.
const int maxDeltaBytes = 4096;

/// The most options one choice in a conversation may offer.
const int maxChoices = 8;

/// The most different items one side of a trade may offer. An inventory has
/// one slot for each.
const int maxTradeLines = inventorySize;

/// The most prayers that are on at once: one for each combat stat.
final int maxActivePrayers = CombatStat.values.length;

bool isValidName(String name) => _name.hasMatch(name);

/// What two names must share to be the same player: their letters, whatever
/// the case.
String nameKey(String name) => name.toLowerCase();

/// A line a player may read: not empty, within [maxTextBytes], and free of
/// control characters.
bool isValidText(String text) {
  if (text.isEmpty || utf8.encode(text).length > maxTextBytes) return false;
  return !text.runes.any((r) => r < 0x20 || r == 0x7f);
}

/// A message a client sends the server.
sealed class ClientMessage {
  const ClientMessage();
}

/// The credentials in a join. A server with accounts checks [password]
/// against the account named [name], or makes the account when [create] is
/// set. A server without accounts reads only [name].
final class Login extends ClientMessage {
  Login(this.name, {this.password = '', this.create = false}) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
    if (utf8.encode(password).length > maxPasswordBytes) {
      throw ArgumentError.value(
        password.length,
        'password',
        'is over $maxPasswordBytes bytes',
      );
    }
  }

  final String name;
  final String password;
  final bool create;
}

/// What a player asks to do. The server turns it into an action.
sealed class Intent extends ClientMessage {
  const Intent();
}

final class Walk extends Intent {
  const Walk(this.target);

  final TileCoord target;
}

/// Use the thing [id] names, in the way [option] picks. What [kind] and
/// [option] mean belongs to the interactions that read them.
final class Interact extends Intent {
  Interact({required this.kind, required this.id, required this.option}) {
    _inRange(kind, 'kind', 0xFF);
    _inRange(id, 'id', 0xFFFFFFFF);
    _inRange(option, 'option', 0xFF);
  }

  final int kind;
  final int id;
  final int option;
}

final class Chat extends Intent {
  Chat(this.text) {
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid chat text');
    }
  }

  final String text;
}

/// A private message to the player called [to], wherever they are.
final class Whisper extends Intent {
  Whisper({required this.to, required this.text}) {
    if (!isValidName(to)) {
      throw ArgumentError.value(to, 'to', 'is not a valid name');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid chat text');
    }
  }

  final String to;
  final String text;
}

/// The two lists a player keeps of other players.
enum ListKind { friends, ignored }

/// Puts [name] on a list, or takes it off.
final class EditList extends Intent {
  EditList({required this.list, required this.add, required this.name}) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
  }

  final ListKind list;
  final bool add;
  final String name;
}

/// An item and a count, which a trade with a shop names.
sealed class Trade extends Intent {
  Trade({required this.item, required this.count}) {
    _checkItem(item);
    _checkCount(count);
  }

  final int item;
  final int count;
}

/// Buy [count] of [item] from the shop the player has open.
final class Buy extends Trade {
  Buy({required super.item, required super.count});
}

/// Sell [count] of [item] to the shop the player has open.
final class Sell extends Trade {
  Sell({required super.item, required super.count});
}

/// Put up to [count] of the inventory stack in [slot] into the bank, in
/// bank tab [tab].
final class Deposit extends Intent {
  Deposit({required this.slot, required this.count, required this.tab}) {
    _inRange(slot, 'slot', 0xFFFF);
    _checkCount(count);
    _inRange(tab, 'tab', 0xFF);
  }

  final int slot;
  final int count;
  final int tab;
}

/// Take up to [count] of the bank stack in [slot] into the inventory.
final class Withdraw extends Intent {
  Withdraw({required this.slot, required this.count}) {
    _inRange(slot, 'slot', 0xFFFF);
    _checkCount(count);
  }

  final int slot;
  final int count;
}

/// Offer [count] of [item] in the trade the player has open, in place of
/// what they offered of it. A count of zero takes it back. Any change sets
/// both players' accepts back.
final class TradeOffer extends Intent {
  TradeOffer({required this.item, required this.count}) {
    _checkItem(item);
    _inRange(count, 'count', maxStack);
  }

  final int item;
  final int count;
}

/// Agree to the trade as it stands. The first agreement of both players
/// moves the trade to its confirm step. The second one makes the swap.
final class TradeAccept extends Intent {
  const TradeAccept();
}

/// Leave the trade the player has open. Nothing changes hands.
final class TradeDecline extends Intent {
  const TradeDecline();
}

/// Cast the spell [spell] names at the creature [target] names. A spell that
/// works on the caster, such as a teleport, ignores [target].
final class Cast extends Intent {
  Cast({required this.spell, required this.target}) {
    _inRange(spell, 'spell', maxSpellId);
    _inRange(target, 'target', 0xFFFFFFFF);
  }

  final int spell;
  final int target;
}

/// Switch the prayer [prayer] names on, or off if it is on. A prayer that
/// raises the same stat goes off when this one comes on.
final class TogglePrayer extends Intent {
  TogglePrayer(this.prayer) {
    if (prayer < 1 || prayer > maxPrayerId) {
      throw ArgumentError.value(
        prayer,
        'prayer',
        'is outside 1 to $maxPrayerId',
      );
    }
  }

  final int prayer;
}

/// Go on from the line a conversation shows.
final class Continue extends Intent {
  const Continue();
}

/// Pick the option at [index] of the choices a conversation last offered.
final class Choose extends Intent {
  Choose(this.index) {
    _inRange(index, 'index', 0xFF);
  }

  final int index;
}

/// A message the server sends a client.
sealed class ServerMessage {
  const ServerMessage();
}

/// The login was taken. [name] is the name the player is known by.
final class LoginOk extends ServerMessage {
  LoginOk(this.name) {
    if (!isValidName(name)) {
      throw ArgumentError.value(name, 'name', 'is not a valid name');
    }
  }

  final String name;
}

/// Which replicated entity is the player's own, by network id. The client
/// follows it with the camera.
final class Avatar extends ServerMessage {
  Avatar(this.networkId) {
    if (networkId < 0 || networkId > 0xFFFFFFFF) {
      throw ArgumentError.value(networkId, 'networkId', 'does not fit a u32');
    }
  }

  final int networkId;
}

/// A change to what only the owner sees, such as items and skills. What the
/// bytes mean belongs to the feature that wrote them.
final class PrivateState extends ServerMessage {
  PrivateState(this.delta) {
    if (delta.length > maxDeltaBytes) {
      throw ArgumentError.value(delta.length, 'delta', 'is over the cap');
    }
  }

  final Uint8List delta;
}

final class Notice extends ServerMessage {
  Notice(this.text) {
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final String text;
}

/// A swing landed on, or missed, the entity [target] names. A [damage] of
/// zero is a miss. The client draws it as a splat over the target.
final class Hit extends ServerMessage {
  Hit({required this.target, required this.damage}) {
    if (target < 0 || target > 0xFFFFFFFF) {
      throw ArgumentError.value(target, 'target', 'does not fit a u32');
    }
    if (damage < 0 || damage > 0xFFFF) {
      throw ArgumentError.value(damage, 'damage', 'does not fit a u16');
    }
  }

  final int target;
  final int damage;
}

/// A projectile leaves the entity [from] names and reaches the one [to]
/// names after [ticks] ticks. The hit it carries arrives with it, as a
/// [Hit].
final class Projectile extends ServerMessage {
  Projectile({
    required this.from,
    required this.to,
    required this.ticks,
    required this.style,
  }) {
    _inRange(from, 'from', 0xFFFFFFFF);
    _inRange(to, 'to', 0xFFFFFFFF);
    if (ticks < 1 || ticks > 0xFF) {
      throw ArgumentError.value(ticks, 'ticks', 'is outside 1 to 255');
    }
  }

  final int from;
  final int to;
  final int ticks;
  final CombatStyle style;
}

/// The prayer points a player has, the most they may have and the prayers
/// that are on. It is sent whenever any of it changes.
final class PrayerView extends ServerMessage {
  PrayerView({
    required this.points,
    required this.max,
    required List<int> active,
  }) : active = List.unmodifiable(active) {
    _inRange(max, 'max', 0xFFFF);
    _inRange(points, 'points', max);
    if (this.active.length > maxActivePrayers ||
        this.active.toSet().length != this.active.length) {
      throw ArgumentError.value(
        active,
        'active',
        'is not a set of up to three',
      );
    }
    for (final id in this.active) {
      if (id < 1 || id > maxPrayerId) {
        throw ArgumentError.value(id, 'active', 'holds a bad prayer id');
      }
    }
  }

  final int points;
  final int max;
  final List<int> active;
}

/// A line of public chat, as the listener hears it. The client draws it over
/// the entity [speaker] names.
final class Said extends ServerMessage {
  Said({required this.speaker, required this.text}) {
    if (speaker < 0 || speaker > 0xFFFFFFFF) {
      throw ArgumentError.value(speaker, 'speaker', 'does not fit a u32');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final int speaker;
  final String text;
}

/// A private message from the player called [from].
final class Whispered extends ServerMessage {
  Whispered({required this.from, required this.text}) {
    if (!isValidName(from)) {
      throw ArgumentError.value(from, 'from', 'is not a valid name');
    }
    if (!isValidText(text)) {
      throw ArgumentError.value(text, 'text', 'is not valid text');
    }
  }

  final String from;
  final String text;
}

/// A friend and whether they are logged in.
typedef Friend = ({String name, bool online});

/// A player's friends, with who is online, and the names they ignore. It is
/// sent whole whenever any of it changes.
final class SocialLists extends ServerMessage {
  SocialLists({required List<Friend> friends, required List<String> ignored})
    : friends = List.unmodifiable(friends),
      ignored = List.unmodifiable(ignored) {
    if (friends.length > maxListEntries || ignored.length > maxListEntries) {
      throw ArgumentError('A list holds at most $maxListEntries names.');
    }
    for (final name in [...friends.map((f) => f.name), ...ignored]) {
      if (!isValidName(name)) {
        throw ArgumentError.value(name, 'name', 'is not a valid name');
      }
    }
  }

  final List<Friend> friends;
  final List<String> ignored;
}

/// One line of a shop's stock: an item and how many the shop holds.
typedef ShopStock = ({int item, int count});

/// What the shop of the creature kind [npc] holds now. It is sent when a
/// shop opens and again each time its stock changes.
final class ShopView extends ServerMessage {
  ShopView({required this.npc, required List<ShopStock> lines})
    : lines = List.unmodifiable(lines) {
    _inRange(npc, 'npc', 0xFFFF);
    if (lines.length > maxShopLines) {
      throw ArgumentError('A shop holds at most $maxShopLines lines.');
    }
    for (final (:item, :count) in lines) {
      _inRange(item, 'item', maxItemId);
      _inRange(count, 'count', maxShopStock);
    }
  }

  final int npc;
  final List<ShopStock> lines;
}

/// The player's bank is open. The slots reach them as container state.
final class BankOpen extends ServerMessage {
  const BankOpen();
}

/// A shop, bank, trade or conversation window is no longer open.
final class WindowClosed extends ServerMessage {
  const WindowClosed();
}

/// The trade the player has open with [partner]: what each side offers,
/// whether this is the confirm step, and who has accepted this step. It is
/// sent each time any of that changes.
final class TradeView extends ServerMessage {
  TradeView({
    required this.partner,
    required List<ItemStack> mine,
    required List<ItemStack> theirs,
    required this.confirming,
    required this.youAccepted,
    required this.theyAccepted,
  }) : mine = List.unmodifiable(mine),
       theirs = List.unmodifiable(theirs) {
    if (!isValidName(partner)) {
      throw ArgumentError.value(partner, 'partner', 'is not a valid name');
    }
    if (mine.length > maxTradeLines || theirs.length > maxTradeLines) {
      throw ArgumentError('A trade side offers at most $maxTradeLines items.');
    }
  }

  final String partner;
  final List<ItemStack> mine;
  final List<ItemStack> theirs;
  final bool confirming;
  final bool youAccepted;
  final bool theyAccepted;
}

/// [speaker] says [text] in the conversation the player has open. The player
/// answers with [Continue].
final class ConversationLine extends ServerMessage {
  ConversationLine({required this.speaker, required this.text}) {
    if (!isValidText(speaker) || !isValidText(text)) {
      throw ArgumentError('A conversation line needs a speaker and text.');
    }
  }

  final String speaker;
  final String text;
}

/// What the player may pick in the conversation they have open. The player
/// answers with [Choose] and the index of one of [options].
final class ConversationChoices extends ServerMessage {
  ConversationChoices(List<String> options)
    : options = List.unmodifiable(options) {
    if (options.isEmpty || options.length > maxChoices) {
      throw ArgumentError.value(options.length, 'options', 'is not 1 to 8');
    }
    if (!options.every(isValidText)) {
      throw ArgumentError.value(options, 'options', 'holds invalid text');
    }
  }

  final List<String> options;
}

/// Why a login was refused. It reaches the client as the text of the
/// goodbye the host sends a connection it turns away, because a refused
/// connection is never a client and so cannot be sent a message.
enum LoginRefusal {
  /// A player is already logged in under that name.
  nameTaken,

  /// The bytes were not a login.
  unreadable,

  /// No account has that name, or the password is wrong. One answer for
  /// both, so a guess does not learn which names exist.
  badLogin,

  /// A new account's password is under [minPasswordBytes] bytes.
  weakPassword,

  /// An account is already made under that name.
  accountExists,

  /// The server is checking as many passwords as it will at once. Try again.
  busy,

  /// The server could not load the account or its character. Nothing was
  /// changed, so trying again later is safe.
  unavailable;

  static LoginRefusal? fromWire(String reason) => values.asNameMap()[reason];
}

/// Why bytes were not turned into a message.
enum BadMessage { empty, unknownTag, tooLarge, malformed }

/// What a decoder made of some bytes. It never throws, since the bytes come
/// from the other end of a connection.
sealed class Decoded<T> {
  const Decoded();
}

final class Understood<T> extends Decoded<T> {
  const Understood(this.message);

  final T message;
}

final class Unreadable<T> extends Decoded<T> {
  const Unreadable(this.why);

  final BadMessage why;
}

void _checkItem(int item) {
  if (item < 1 || item > maxItemId) {
    throw ArgumentError.value(item, 'item', 'is outside 1 to $maxItemId');
  }
}

void _checkCount(int count) {
  if (count < 1 || count > maxStack) {
    throw ArgumentError.value(count, 'count', 'is outside 1 to $maxStack');
  }
}

void _inRange(int value, String name, int max) {
  if (value < 0 || value > max) {
    throw ArgumentError.value(value, name, 'is outside 0 to $max');
  }
}
