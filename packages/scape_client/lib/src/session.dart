import 'dart:typed_data';

import 'package:orblit_core/orblit_core.dart';
import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_progress/orblit_progress.dart';
import 'package:scape_core/scape_core.dart';

/// A little over one tick, so the client always holds a state on each side
/// of the time it draws.
const Duration interpolationDelay = Duration(milliseconds: 700);

/// How long a line of chat stays over a head.
const double bubbleSeconds = 5;

/// The most notices and whispers kept. The oldest go first.
const int _keepLines = 100;

/// One actor as the client draws it.
typedef Actor = ({int networkId, double x, double y, int plane, bool mine});

/// An item lying on the ground, with the position it is drawn at now.
typedef GroundSprite = ({
  int networkId,
  int item,
  int count,
  double x,
  double y,
  int plane,
});

/// An object standing in view, with the position it is drawn at now.
typedef ObjectSprite = ({
  int networkId,
  int def,
  int state,
  double x,
  double y,
  int plane,
});

/// A creature standing in view, with the position it is drawn at now.
typedef NpcSprite = ({int networkId, int def, double x, double y, int plane});

/// A line of public chat to draw over the actor [networkId] is.
typedef Bubble = ({int networkId, String text});

/// A swing that landed on the actor [target] names. A [damage] of zero is a
/// miss.
typedef Splat = ({int target, int damage});

/// A shot or spell in the air from the actor [from] to the actor [to]. It
/// lands after [ticks] ticks.
typedef Shot = ({int from, int to, int ticks, CombatStyle style});

/// The hit points an actor has and the most it can have.
typedef Health = ({int now, int max});

/// A private message from the player called [from].
typedef Heard = ({String from, String text});

/// A line of public chat, as the listener heard it.
typedef Spoken = ({int speaker, String text});

/// The tabs of the chat box. Game is what the server tells the player, public
/// is what players near them said, and private is whispers.
enum ChatTab { game, public, private }

/// A quest the player has begun, with the text for the stage they are in.
typedef JournalEntry = ({String title, String text, bool done});

/// The client's side of one login: what it sends, what it has been told and
/// the actors it can see. It holds no rules and draws what it is sent.
final class GameSession {
  GameSession({
    required Transport transport,
    required SkillSet skills,
    required ItemSet items,
    Story? story,
    double Function()? clock,
  }) : _transport = transport,
       _skills = skills,
       _items = items,
       _quests = story == null
           ? null
           : (story: story, table: ProgressTable(story.schema)),
       _skillXp = ProgressTable(skills.schema),
       _containers = {
         for (final id in ContainerId.values) id: Container.forId(id, items),
       },
       _now = clock ?? _stopwatchClock(),
       _world = World() {
    final networkId = _world.registerComponent(
      'NetworkId',
      kind: ComponentKind.int64,
    );
    _position = _world.registerComponent(
      tilePositionName,
      kind: ComponentKind.float32,
      arity: tilePositionFloats,
    );
    _ground = _world.registerComponent(
      groundItemName,
      kind: ComponentKind.int32,
      arity: groundItemInts,
    );
    _object = _world.registerComponent(
      worldObjectName,
      kind: ComponentKind.int32,
      arity: worldObjectInts,
    );
    _npc = _world.registerComponent(
      npcName,
      kind: ComponentKind.int32,
      arity: npcInts,
    );
    _health = _world.registerComponent(
      healthName,
      kind: ComponentKind.int32,
      arity: healthInts,
    );
    _look = _world.registerComponent(
      lookName,
      kind: ComponentKind.int32,
      arity: lookInts,
    );
    _anim = _world.registerComponent(
      animName,
      kind: ComponentKind.int32,
      arity: animInts,
    );
    _client = NetClient(
      world: _world,
      set: ReplicationSet([
        _position,
        _ground,
        _object,
        _npc,
        _health,
        _look,
        _anim,
      ]),
      networkId: networkId,
      transport: transport,
      interpolationDelay: interpolationDelay,
      clock: _now,
      onMessage: _onMessage,
      onGoodbye: (reason) => refusal = reason,
    );
  }

  /// Opens a connection to the server at [url]. The session closes it on
  /// [dispose].
  static Future<GameSession> connect(
    Uri url, {
    required SkillSet skills,
    required ItemSet items,
    Story? story,
    double Function()? clock,
  }) async => GameSession(
    transport: await SocketTransport.connect(url),
    skills: skills,
    items: items,
    story: story,
    clock: clock,
  );

  static double Function() _stopwatchClock() {
    final stopwatch = Stopwatch()..start();
    return () => stopwatch.elapsedMicroseconds / 1e6;
  }

  final Transport _transport;
  final SkillSet _skills;
  final ItemSet _items;
  final Map<ContainerId, Container> _containers;
  ProgressTable _skillXp;
  ({Story story, ProgressTable table})? _quests;
  final double Function() _now;
  final World _world;
  late final ComponentType _position;
  late final ComponentType _ground;
  late final ComponentType _object;
  late final ComponentType _npc;
  late final ComponentType _health;
  late final ComponentType _look;
  late final ComponentType _anim;
  late final NetClient _client;
  final List<String> _notices = [];
  final List<Heard> _whispers = [];
  final List<Spoken> _said = [];
  final List<Splat> _splats = [];
  final List<Shot> _shots = [];
  final Map<int, ({String text, double until})> _bubbles = {};

  /// The name the server knows the player by, once logged in.
  String? name;

  /// Which replicated entity is the player's own, once the server says.
  int? avatar;

  /// Why the server sent the player away, if it did.
  String? refusal;

  /// Friends, with whether each is logged in. Empty until the server sends it.
  List<Friend> friends = const [];

  /// Names the player ignores.
  List<String> ignored = const [];

  /// The shop the player has open, as the server last sent it, or null. A
  /// shop, a bank and a conversation are never open together.
  ShopView? shop;

  /// The trade with another player the player has open, as the server last
  /// sent it, or null. Never open together with a shop, a bank or a
  /// conversation.
  TradeView? playerTrade;

  /// The player's prayer points and which prayers are on, as the server last
  /// sent them, or null before it has.
  PrayerView? prayer;

  /// Whether the player's bank is open. [itemAt] reads what is in it.
  bool bankOpen = false;

  /// The line a creature is saying in the open conversation, or null.
  ConversationLine? line;

  /// What the player may pick in the open conversation, in the order the
  /// server sent. Empty when it is showing a line or none is open.
  List<String> choices = const [];

  List<String> get notices => List.unmodifiable(_notices);

  /// The lines of [tab], oldest first, as the chat box shows them. The server
  /// sends no names with public chat, so a line reads "You" or "Someone".
  List<String> chat(ChatTab tab) => switch (tab) {
    ChatTab.game => notices,
    ChatTab.private => [for (final (:from, :text) in _whispers) '$from: $text'],
    ChatTab.public => [
      for (final (:speaker, :text) in _said)
        '${speaker == avatar ? 'You' : 'Someone'}: $text',
    ],
  };

  /// The quests the player has begun, with the text for the stage they are in.
  /// Empty when the session was made without a story.
  List<JournalEntry> get journal {
    final held = _quests;
    if (held == null) return const [];
    final book = held.story.book;
    return [
      for (final quest in book.quests)
        if (book.isStarted(held.table, quest.key))
          (
            title: quest.title,
            text: book.journal(held.table, quest.key),
            done: book.isComplete(held.table, quest.key),
          ),
    ];
  }

  /// Swings that landed or missed, oldest first.
  List<Splat> get splats => List.unmodifiable(_splats);

  /// Shots and spells that were sent, oldest first.
  List<Shot> get shots => List.unmodifiable(_shots);

  /// The hit points of the actor [networkId] names, or null for one that is
  /// not in view or does not fight.
  Health? healthOf(int networkId) {
    final entity = _client.entityFor(networkId);
    final bytes = entity == null ? null : _world.bytesOf(entity, _health);
    if (bytes == null) return null;
    final data = ByteData.sublistView(bytes);
    return (
      now: data.getInt32(0, Endian.little),
      max: data.getInt32(4, Endian.little),
    );
  }

  /// How the actor [networkId] names looks, or null for one that is not in
  /// view or is not a player.
  Look? lookOf(int networkId) {
    final entity = _client.entityFor(networkId);
    final bytes = entity == null ? null : _world.bytesOf(entity, _look);
    return bytes == null ? null : tryReadLook(bytes);
  }

  /// What the actor [networkId] names is doing, or null for one that is not in
  /// view.
  Anim? animOf(int networkId) {
    final entity = _client.entityFor(networkId);
    final bytes = entity == null ? null : _world.bytesOf(entity, _anim);
    return bytes == null ? null : tryReadAnim(bytes);
  }

  /// Private messages received, oldest first.
  List<Heard> get whispers => List.unmodifiable(_whispers);

  /// The chat lines to draw now, one per speaker, the newest of each. A speaker
  /// is one entry for as long as the session lasts, which is small.
  List<Bubble> get bubbles => [
    for (final MapEntry(:key, :value) in _bubbles.entries)
      if (value.until > _now()) (networkId: key, text: value.text),
  ];

  bool get isLoggedIn => name != null;

  /// The experience the server has told the player they hold in [skill].
  /// Throws [ArgumentError] for a skill the game does not have.
  int xpOf(String skill) => _skillXp.get(SkillSet.xpName(_skill(skill).id));

  /// The level that experience gives, up to the skill's cap.
  int levelOf(String skill) => _skills.levelOf(_skill(skill), xpOf(skill));

  /// What the server has told the player is in [slot] of [container], or null
  /// for an empty slot and for a slot that does not exist.
  ItemStack? itemAt(ContainerId container, int slot) =>
      _containers[container]!.at(slot);

  SkillDef _skill(String id) =>
      _skills.find(id) ??
      (throw ArgumentError.value(id, 'skill', 'is unknown'));

  /// Joins as [player]. A server with accounts wants the [password], and
  /// [create] makes the account first.
  void login(String player, {String password = '', bool create = false}) =>
      _client.join(
        encodeClientMessage(Login(player, password: password, create: create)),
      );

  void walkTo(TileCoord tile) =>
      _client.sendMessage(encodeClientMessage(Walk(tile)));

  /// Picks option [option] of the object [networkId].
  void useObject(int networkId, int option) => _interact(
    Interact(kind: InteractKind.object, id: networkId, option: option),
  );

  /// Picks up the item lying on the ground that [networkId] names.
  void takeItem(int networkId) =>
      _interact(Interact(kind: InteractKind.ground, id: networkId, option: 0));

  /// Attacks the creature that [networkId] names.
  void attack(int networkId) => _interact(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.attack),
  );

  /// Casts the spell [spell] at the creature that [networkId] names.
  void cast(int spell, int networkId) => _client.sendMessage(
    encodeClientMessage(Cast(spell: spell, target: networkId)),
  );

  /// Switches the prayer [prayer] on or off.
  void togglePrayer(int prayer) =>
      _client.sendMessage(encodeClientMessage(TogglePrayer(prayer)));

  /// Opens the shop of the creature that [networkId] names.
  void trade(int networkId) => _interact(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.trade),
  );

  /// Talks to the creature that [networkId] names.
  void talk(int networkId) => _interact(
    Interact(kind: InteractKind.npc, id: networkId, option: NpcOption.talk),
  );

  /// Goes on from the line in the open conversation.
  void continueTalk() =>
      _client.sendMessage(encodeClientMessage(const Continue()));

  /// Picks the option at [index] of [choices].
  void choose(int index) =>
      _client.sendMessage(encodeClientMessage(Choose(index)));

  /// Asks the player [networkId] to trade. When they ask back, a trade opens
  /// and [playerTrade] is set.
  void requestTrade(int networkId) =>
      _interact(Interact(kind: InteractKind.player, id: networkId, option: 0));

  /// Offers [count] of [item] in the open trade, in place of what was
  /// offered of it. A count of zero takes it back.
  void offer(int item, int count) => _client.sendMessage(
    encodeClientMessage(TradeOffer(item: item, count: count)),
  );

  /// Agrees to the open trade as it stands.
  void acceptTrade() =>
      _client.sendMessage(encodeClientMessage(const TradeAccept()));

  /// Leaves the open trade.
  void declineTrade() =>
      _client.sendMessage(encodeClientMessage(const TradeDecline()));

  /// Buys [count] of [item] from the open shop.
  void buy(int item, int count) =>
      _client.sendMessage(encodeClientMessage(Buy(item: item, count: count)));

  /// Sells [count] of [item] to the open shop.
  void sell(int item, int count) =>
      _client.sendMessage(encodeClientMessage(Sell(item: item, count: count)));

  /// Puts up to [count] of the inventory stack in [slot] into bank [tab].
  void deposit(int slot, int count, {int tab = 0}) => _client.sendMessage(
    encodeClientMessage(Deposit(slot: slot, count: count, tab: tab)),
  );

  /// Takes up to [count] of the bank stack in [slot] into the inventory.
  void withdraw(int slot, int count) => _client.sendMessage(
    encodeClientMessage(Withdraw(slot: slot, count: count)),
  );

  /// Uses the inventory item in [slot] on the object [networkId].
  void useItemOnObject(int slot, int networkId) => _interact(
    Interact(kind: InteractKind.useOnObject, id: networkId, option: slot),
  );

  /// Uses the inventory item in [slot] on the one in [onSlot].
  void useItemOnItem(int slot, int onSlot) => _interact(
    Interact(kind: InteractKind.useOnItem, id: onSlot, option: slot),
  );

  /// Drags the stack in [fromSlot] of [from] onto [toSlot] of [to]. Nothing
  /// changes here. The slots change when the server sends them, so a move it
  /// refuses leaves the panel as it was.
  void moveItem(ContainerId from, int fromSlot, ContainerId to, int toSlot) =>
      _client.sendMessage(
        encodeClientMessage(
          MoveItem(from: from, fromSlot: fromSlot, to: to, toSlot: toSlot),
        ),
      );

  void _interact(Interact click) =>
      _client.sendMessage(encodeClientMessage(click));

  /// Says [text] to the players nearby. The text must satisfy [isValidText].
  void say(String text) => _client.sendMessage(encodeClientMessage(Chat(text)));

  /// Sends [text] privately to the player called [to]. Both must be valid.
  void whisper(String to, String text) =>
      _client.sendMessage(encodeClientMessage(Whisper(to: to, text: text)));

  /// Puts [name] on [list], or takes it off. The name must satisfy
  /// [isValidName].
  void editList(ListKind list, String name, {required bool add}) =>
      _client.sendMessage(
        encodeClientMessage(EditList(list: list, add: add, name: name)),
      );

  /// Blends the world towards where the server was a moment ago. Call once a
  /// frame.
  void advance() => _client.advance();

  /// Every actor in view, with the position it is drawn at now.
  List<Actor> get actors => [
    for (final networkId in _client.networkIds)
      if (_isActor(networkId)) _actorFor(networkId),
  ];

  /// Every object in view, such as a door or a tree, with its state.
  List<ObjectSprite> get objects => [
    for (final networkId in _client.networkIds)
      if (_isObject(networkId)) _objectFor(networkId),
  ];

  /// Every creature in view, such as a chicken or a guard.
  List<NpcSprite> get npcs => [
    for (final networkId in _client.networkIds)
      if (_isNpc(networkId)) _npcFor(networkId),
  ];

  /// Every item lying in view, with the position it is drawn at now.
  List<GroundSprite> get groundItems => [
    for (final networkId in _client.networkIds)
      if (_isGround(networkId)) _spriteFor(networkId),
  ];

  bool _isGround(int networkId) =>
      _world.has(_client.entityFor(networkId)!, _ground);

  bool _isNpc(int networkId) => _world.has(_client.entityFor(networkId)!, _npc);

  bool _isActor(int networkId) =>
      !_isGround(networkId) && !_isObject(networkId) && !_isNpc(networkId);

  NpcSprite _npcFor(int networkId) {
    final entity = _client.entityFor(networkId)!;
    final point = readTilePoint(_world.bytesOf(entity, _position)!);
    return (
      networkId: networkId,
      def: ByteData.sublistView(
        _world.bytesOf(entity, _npc)!,
      ).getInt32(0, Endian.little),
      x: point.x,
      y: point.y,
      plane: point.plane,
    );
  }

  bool _isObject(int networkId) =>
      _world.has(_client.entityFor(networkId)!, _object);

  ObjectSprite _objectFor(int networkId) {
    final entity = _client.entityFor(networkId)!;
    final point = readTilePoint(_world.bytesOf(entity, _position)!);
    final data = ByteData.sublistView(_world.bytesOf(entity, _object)!);
    return (
      networkId: networkId,
      def: data.getInt32(0, Endian.little),
      state: data.getInt32(4, Endian.little),
      x: point.x,
      y: point.y,
      plane: point.plane,
    );
  }

  GroundSprite _spriteFor(int networkId) {
    final entity = _client.entityFor(networkId)!;
    final point = readTilePoint(_world.bytesOf(entity, _position)!);
    final data = ByteData.sublistView(_world.bytesOf(entity, _ground)!);
    return (
      networkId: networkId,
      item: data.getInt32(0, Endian.little),
      count: data.getInt32(4, Endian.little),
      x: point.x,
      y: point.y,
      plane: point.plane,
    );
  }

  Actor _actorFor(int networkId) {
    final bytes = _world.bytesOf(_client.entityFor(networkId)!, _position)!;
    final point = readTilePoint(bytes);
    return (
      networkId: networkId,
      x: point.x,
      y: point.y,
      plane: point.plane,
      mine: networkId == avatar,
    );
  }

  void _onMessage(Uint8List body) {
    if (decodeServerMessage(body) case Understood(:final message)) {
      switch (message) {
        case LoginOk(:final name):
          this.name = name;
        case Avatar(:final networkId):
          avatar = networkId;
        case Notice(:final text):
          _keep(_notices, text);
        case Said(:final speaker, :final text):
          _bubbles[speaker] = (text: text, until: _now() + bubbleSeconds);
          _keep(_said, (speaker: speaker, text: text));
        case Hit(:final target, :final damage):
          _keep(_splats, (target: target, damage: damage));
        case Projectile(:final from, :final to, :final ticks, :final style):
          _keep(_shots, (from: from, to: to, ticks: ticks, style: style));
        case PrayerView():
          prayer = message;
        case Whispered(:final from, :final text):
          _keep(_whispers, (from: from, text: text));
        case SocialLists(:final friends, :final ignored):
          this.friends = friends;
          this.ignored = ignored;
        case PrivateState(:final delta):
          _applyPrivate(delta);
        case ShopView():
          _closeWindow();
          shop = message;
        case TradeView():
          _closeWindow();
          playerTrade = message;
        case BankOpen():
          _closeWindow();
          bankOpen = true;
        case ConversationLine():
          _closeWindow();
          line = message;
        case ConversationChoices(:final options):
          _closeWindow();
          choices = options;
        case WindowClosed():
          _closeWindow();
      }
    }
  }

  void _closeWindow() {
    shop = null;
    playerTrade = null;
    bankOpen = false;
    line = null;
    choices = const [];
  }

  void _applyPrivate(Uint8List delta) {
    if (delta.isEmpty) return;
    switch (delta[0]) {
      case skillsDeltaTag:
        final changes = Changes.tryDecode(_skills.schema, delta.sublist(1));
        if (changes != null) _skillXp = _skillXp.apply(changes);
      case questsDeltaTag:
        _applyQuests(delta.sublist(1));
      case itemsDeltaTag:
        if (tryDecodeSlots(delta, _items) case final slots?) {
          _containers[slots.container]!.mirror(slots.changes);
        }
    }
  }

  void _applyQuests(Uint8List bytes) {
    final held = _quests;
    if (held == null) return;
    final changes = Changes.tryDecode(held.story.schema, bytes);
    if (changes != null) {
      _quests = (story: held.story, table: held.table.apply(changes));
    }
  }

  void _keep<T>(List<T> lines, T line) {
    lines.add(line);
    if (lines.length > _keepLines) lines.removeAt(0);
  }

  Future<void> dispose() async {
    await _client.dispose();
    await _transport.close();
    _world.dispose();
  }
}
