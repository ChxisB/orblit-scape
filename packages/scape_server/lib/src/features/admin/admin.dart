import 'dart:async';
import 'dart:convert';

import 'package:scape_core/scape_core.dart';

import '../../engine/phase.dart';
import '../../engine/registry.dart';
import '../../engine/services.dart';
import '../items/items.dart';
import '../movement/movement.dart';
import '../skills/skills.dart';
import '../social/social.dart';
import 'ledger.dart';

/// What a player may do with the commands. A command names the lowest role
/// that may run it.
enum Role { player, moderator, admin }

/// Seconds left at which a shutdown countdown tells everyone.
const Set<int> _warnAt = {60, 30, 10, 5, 4, 3, 2, 1};

const String _noReason = 'no reason given';

/// The largest number a command takes, so a minute count cannot overflow.
const int _maxNumber = 1 << 30;

final RegExp _word = RegExp(r'"([^"]*)"|(\S+)');

final RegExp _control = RegExp(r'[\x00-\x1f\x7f]');

/// A command that was written wrong, or asked for what cannot be done.
final class _Usage implements Exception {
  const _Usage(this.message);

  final String message;
}

/// The words of one command line, read in order. A word in double quotes may
/// hold spaces, so a name with a space can be named.
final class _Call {
  _Call(this.by, Iterable<String> words, this.usage) : _words = words.toList();

  final String by;
  final String usage;
  final List<String> _words;
  int _next = 0;

  String word() {
    if (_next >= _words.length) throw _Usage('Use $usage');
    return _words[_next++];
  }

  int number({int min = 0, int? otherwise}) {
    if (_next >= _words.length && otherwise != null) return otherwise;
    final value = int.tryParse(word());
    if (value == null || value < min || value > _maxNumber) {
      throw _Usage('Use $usage');
    }
    return value;
  }

  /// What is left, joined, or [otherwise] when nothing is.
  String rest({String? otherwise}) {
    final text = _words.skip(_next).join(' ');
    _next = _words.length;
    if (text.isNotEmpty) return text;
    return otherwise ?? (throw _Usage('Use $usage'));
  }
}

/// Whom a command was for, what it did and what the caller is told. The log
/// line is made from it, so a command cannot forget to write one.
typedef _Done = ({String whom, String why, String told});

typedef _Command = ({
  Role role,
  String usage,
  FutureOr<_Done> Function(_Call call) run,
});

/// Commands typed as chat lines that start with [commandMarker]. The server
/// reads them, never the client, and a player who holds no role is told the
/// command does not exist.
///
/// Mutes and bans are kept in the [Ledger], which saves them, so they hold
/// across a restart. A mute is applied to chat each tick from the ledger, so
/// it ends by itself.
final class Admin {
  Admin._(
    this._registry,
    this._services, {
    required Map<String, Role> roles,
    required Ledger ledger,
    required Social social,
    required Movement movement,
    required Skills skills,
    required SkillSet skillSet,
    required Items bags,
    required ItemSet items,
    required Future<String> Function(String player, int minutes)? rollback,
    required void Function() stop,
    required void Function(String line) log,
  }) : _roles = roles,
       _ledger = ledger,
       _social = social,
       _movement = movement,
       _skills = skills,
       _skillSet = skillSet,
       _bags = bags,
       _items = items,
       _rollback = rollback,
       _stop = stop,
       _log = log;

  /// Adds the commands to [registry]. [roles] is by name key. [rollback] is
  /// null for a server whose store keeps no history, and [stop] ends the
  /// server when a countdown runs out.
  factory Admin.register(
    Registry registry,
    Services services, {
    required Map<String, Role> roles,
    required Ledger ledger,
    required Social social,
    required Movement movement,
    required Skills skills,
    required SkillSet skillSet,
    required Items bags,
    required ItemSet items,
    required Future<String> Function(String player, int minutes)? rollback,
    required void Function() stop,
    required void Function(String line) log,
  }) {
    final admin = Admin._(
      registry,
      services,
      roles: roles,
      ledger: ledger,
      social: social,
      movement: movement,
      skills: skills,
      skillSet: skillSet,
      bags: bags,
      items: items,
      rollback: rollback,
      stop: stop,
      log: log,
    );
    social.reserve(commandMarker);
    registry
      ..addJoinHandler(admin._players.add)
      ..addGoneHandler(admin._players.remove)
      ..addIntentHandler(admin._onIntent)
      ..addSystem(Phase.outbound, admin._tick);
    return admin;
  }

  final Registry _registry;
  final Services _services;
  final Map<String, Role> _roles;
  final Ledger _ledger;
  final Social _social;
  final Movement _movement;
  final Skills _skills;
  final SkillSet _skillSet;
  final Items _bags;
  final ItemSet _items;
  final Future<String> Function(String player, int minutes)? _rollback;
  final void Function() _stop;
  final void Function(String line) _log;

  final Set<String> _players = {};

  /// The tick a shutdown happens on, or null when none is set.
  int? _stopAt;

  /// The seconds a countdown last told everyone, so it says each once.
  int _warned = 0;

  late final Map<String, _Command> _commands = {
    'kick': (role: Role.moderator, usage: '/kick <name> [reason]', run: _kick),
    'mute': (
      role: Role.moderator,
      usage: '/mute <name> <minutes, 0 for no end> [reason]',
      run: (call) => _sanction(SanctionKind.mute, call),
    ),
    'unmute': (
      role: Role.moderator,
      usage: '/unmute <name>',
      run: (call) => _lift(SanctionKind.mute, call),
    ),
    'ban': (
      role: Role.moderator,
      usage: '/ban <name> <minutes, 0 for no end> [reason]',
      run: (call) => _sanction(SanctionKind.ban, call),
    ),
    'unban': (
      role: Role.moderator,
      usage: '/unban <name>',
      run: (call) => _lift(SanctionKind.ban, call),
    ),
    'teleport': (
      role: Role.admin,
      usage: '/teleport <name> <x> <y> [plane]',
      run: _teleport,
    ),
    'give': (
      role: Role.admin,
      usage: '/give <name> <item id> [count]',
      run: _give,
    ),
    'skill': (
      role: Role.admin,
      usage: '/skill <name> <skill id> <experience>',
      run: _setSkill,
    ),
    'broadcast': (
      role: Role.admin,
      usage: '/broadcast <text>',
      run: _broadcast,
    ),
    'shutdown': (
      role: Role.admin,
      usage: '/shutdown <seconds>',
      run: _shutdown,
    ),
    'cancelshutdown': (
      role: Role.admin,
      usage: '/cancelshutdown',
      run: _cancelShutdown,
    ),
    'rollback': (
      role: Role.admin,
      usage: '/rollback <name> <minutes>',
      run: _rollbackPlayer,
    ),
  };

  void _onIntent(String player, Intent intent) {
    if (intent is! Chat || !intent.text.startsWith(commandMarker)) return;
    unawaited(_run(player, intent.text.substring(commandMarker.length)));
  }

  Future<void> _run(String by, String line) async {
    final words = [
      for (final m in _word.allMatches(line)) m.group(1) ?? m.group(2) ?? '',
    ];
    final name = words.firstOrNull?.toLowerCase() ?? '';
    final command = _commands[name];
    if (command == null ||
        (_roles[by] ?? Role.player).index < command.role.index) {
      _log('Admin: $by was refused "$name"');
      return _tell(by, 'Unknown command.');
    }
    try {
      final done = await command.run(_Call(by, words.skip(1), command.usage));
      _log('Admin: $by $name ${done.whom}: ${done.why}');
      _tell(by, done.told);
    } on _Usage catch (usage) {
      _log('Admin: $by $name failed: ${usage.message}');
      _tell(by, usage.message);
    }
  }

  _Done _kick(_Call call) {
    final target = _online(call.word());
    final why = call.rest(otherwise: _noReason);
    _tell(target, 'You were kicked: $why');
    unawaited(_services.hosting.kick(target));
    return (whom: target, why: why, told: '$target was kicked.');
  }

  _Done _sanction(SanctionKind kind, _Call call) {
    final target = nameKey(call.word());
    final minutes = call.number();
    final why = call.rest(otherwise: _noReason);
    _ledger.add(
      kind,
      target,
      by: call.by,
      reason: why,
      lasting: minutes == 0 ? null : Duration(minutes: minutes),
    );
    if (_players.contains(target)) {
      _tell(target, 'You are ${kind.done}: $why');
      if (kind == SanctionKind.ban) {
        unawaited(_services.hosting.kick(target));
      } else {
        _syncMute(target);
      }
    }
    final span = minutes == 0 ? 'with no end' : 'for $minutes minutes';
    return (
      whom: target,
      why: '$why, $span',
      told: '$target is ${kind.done} $span.',
    );
  }

  _Done _lift(SanctionKind kind, _Call call) {
    final target = nameKey(call.word());
    if (_ledger.active(kind, target) == null) {
      throw _Usage('$target is not ${kind.done}.');
    }
    _ledger.lift(kind, target);
    return (
      whom: target,
      why: 'lifted',
      told: '$target is no longer ${kind.done}.',
    );
  }

  _Done _teleport(_Call call) {
    final target = _online(call.word());
    final x = call.number();
    final y = call.number();
    final plane = call.number(otherwise: 0);
    _movement.teleport(target, _standable(x, y, plane));
    return (
      whom: target,
      why: 'to $x $y $plane',
      told: '$target is at $x, $y.',
    );
  }

  TileCoord _standable(int x, int y, int plane) {
    final TileCoord tile;
    try {
      tile = TileCoord(x, y, plane);
    } on ArgumentError {
      throw const _Usage('That tile is outside the world.');
    }
    if (_services.map.flagsAt(tile).has(TileFlags.blocked | TileFlags.water)) {
      throw const _Usage('Nobody can stand there.');
    }
    return tile;
  }

  _Done _give(_Call call) {
    final target = _online(call.word());
    final id = call.number(min: 1);
    final count = call.number(min: 1, otherwise: 1);
    final def = _items.find(id);
    final bag = _bags.container(target, ContainerId.inventory);
    if (def == null) throw const _Usage('There is no such item.');
    if (bag == null) throw _Usage('$target has no inventory yet.');
    final added = bag.add(id, count);
    _tell(target, 'You were given $added x ${def.name}.');
    return (
      whom: target,
      why: '$added of $count x ${def.name}',
      told: '$target took $added of $count x ${def.name}.',
    );
  }

  _Done _setSkill(_Call call) {
    final target = _online(call.word());
    final id = call.word();
    final xp = call.number();
    if (_skillSet.find(id) == null) {
      throw const _Usage('There is no such skill.');
    }
    _skills.restore(target, {id: xp});
    return (whom: target, why: '$id to $xp', told: '$target has $xp in $id.');
  }

  _Done _broadcast(_Call call) {
    final text = call.rest();
    _announce(text);
    return (whom: 'everyone', why: text, told: 'Sent.');
  }

  _Done _shutdown(_Call call) {
    final seconds = call.number(min: 1);
    final ticks = (seconds * 1000 / tickPeriod.inMilliseconds).ceil();
    _stopAt = _registry.actions.tick + ticks;
    _warned = seconds;
    _announce('The server shuts down in ${_seconds(seconds)}.');
    return (whom: 'everyone', why: 'in ${_seconds(seconds)}', told: 'Set.');
  }

  _Done _cancelShutdown(_Call call) {
    if (_stopAt == null) throw const _Usage('No shutdown is set.');
    _stopAt = null;
    _announce('The shutdown was cancelled.');
    return (whom: 'everyone', why: 'cancelled', told: 'Cancelled.');
  }

  Future<_Done> _rollbackPlayer(_Call call) async {
    final target = nameKey(call.word());
    final minutes = call.number(min: 1);
    final rollback = _rollback;
    if (rollback == null) {
      throw const _Usage('This server keeps no save history.');
    }
    if (_players.contains(target)) {
      _tell(target, 'You are logged out while your save is rolled back.');
    }
    final told = await rollback(target, minutes);
    return (whom: target, why: '$minutes minutes', told: told);
  }

  void _tick() {
    _players.forEach(_syncMute);
    _countdown();
  }

  void _countdown() {
    final at = _stopAt;
    if (at == null) return;
    final left = at - _registry.actions.tick;
    final seconds = (left * tickPeriod.inMilliseconds / 1000).ceil();
    if (seconds <= 0) {
      _stopAt = null;
      _stop();
    } else if (_warnAt.contains(seconds) && seconds != _warned) {
      _warned = seconds;
      _announce('The server shuts down in ${_seconds(seconds)}.');
    }
  }

  /// Makes chat agree with the ledger for [player]: muted while a mute has
  /// not ended, free after.
  void _syncMute(String player) {
    final muted = _ledger.active(SanctionKind.mute, player) != null;
    if (muted == _social.isMuted(player)) return;
    if (muted) {
      _social.mute(player);
    } else {
      _social.unmute(player);
      _tell(player, 'You can chat again.');
    }
  }

  String _online(String name) {
    final key = nameKey(name);
    if (!_players.contains(key)) throw _Usage('$name is not online.');
    return key;
  }

  void _announce(String text) {
    for (final player in _players) {
      _tell(player, '[Server] $text');
    }
  }

  void _tell(String player, String text) =>
      _services.hosting.tell(player, Notice(_fit(text)));
}

String _seconds(int count) => count == 1 ? '1 second' : '$count seconds';

/// [text] as a notice may carry it: no control characters, and no more than
/// [maxTextBytes] bytes.
String _fit(String text) {
  final buffer = StringBuffer();
  var bytes = 0;
  for (final rune in text.replaceAll(_control, ' ').runes) {
    final size = utf8.encode(String.fromCharCode(rune)).length;
    if (bytes + size > maxTextBytes) break;
    bytes += size;
    buffer.writeCharCode(rune);
  }
  return buffer.toString();
}
