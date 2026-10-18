import 'dart:math';

import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';

/// A client program. It logs in over the wire and picks what to do from what
/// the server told it, as a player's client would. It walks to tiles near
/// [home] without looking at the map, so some walks end in walls or off the
/// edge of the world on purpose.
final class Bot {
  Bot({
    required this.name,
    required this.session,
    required this.home,
    required this.reach,
    required Random random,
  }) : _random = random {
    session.login(name);
  }

  final String name;
  final GameSession session;
  final TileCoord home;

  /// How many tiles from [home] a walk may end.
  final int reach;
  final Random _random;

  int _wait = 0;

  bool get isIn => session.avatar != null;

  /// Does one thing now and then. Call once a tick.
  void act() {
    if (!isIn || --_wait > 0) return;
    _wait = 1 + _random.nextInt(6);
    _choices[_random.nextInt(_choices.length)](this);
  }

  void _walk() {
    int near(int at) =>
        (at + _random.nextInt(reach * 2 + 1) - reach).clamp(0, worldSize - 1);
    session.walkTo(TileCoord(near(home.x), near(home.y), home.plane));
  }

  void _fight() {
    final creatures = session.npcs;
    if (creatures.isEmpty) return;
    session.attack(creatures[_random.nextInt(creatures.length)].networkId);
  }

  void _use() {
    final things = session.objects;
    if (things.isEmpty) return;
    session.useObject(things[_random.nextInt(things.length)].networkId, 0);
  }

  static final List<void Function(Bot)> _choices = [
    for (var i = 0; i < 6; i++) (bot) => bot._walk(),
    for (var i = 0; i < 2; i++) (bot) => bot._fight(),
    for (var i = 0; i < 2; i++) (bot) => bot._use(),
  ];
}
