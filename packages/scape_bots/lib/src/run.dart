import 'dart:math';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_client/scape_client.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

import 'bot.dart';
import 'probe.dart';
import 'report.dart';

/// Flat open ground, two regions across and two up, on plane 0.
TileMap openGround() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

final TileCoord _spawn = TileCoord(32, 32, 0);

final class _NoPacer implements Pacer {
  @override
  Future<void> untilNext() async {}
}

/// A server with bots on loopback links. The bots are the same client the
/// app uses, so what they send and read is what a player's client does. The
/// server runs here, one tick at a time.
final class BotRun {
  BotRun._(this.server, this.probe, this.bots, this._problems, this.seed);

  /// Starts a server on [map] with trees and chickens round the spawn tile,
  /// which must be open ground, and logs in [bots] bots. A bot walks up to
  /// [reach] tiles from the spawn. The [seed] fixes the bots and the server's
  /// dice, so the same seed plays out the same way.
  static Future<BotRun> start({
    required int bots,
    int seed = 1,
    int reach = 12,
    TileMap? map,
  }) async {
    final problems = <String>[];
    final random = Random(seed);
    final server = _serverOn(
      map ?? openGround(),
      problems.add,
      Random(random.nextInt(1 << 30)),
    );
    return BotRun._(
      server,
      ServerProbe(server),
      [
        for (var i = 0; i < bots; i++)
          _join(server, 'bot-${i.toString().padLeft(4, '0')}', reach, random),
      ],
      problems,
      seed,
    );
  }

  static ScapeServer _serverOn(
    TileMap map,
    void Function(String) log,
    Random random,
  ) {
    final server = ScapeServer(
      clock: TickClock(period: tickPeriod),
      pacer: _NoPacer(),
      log: log,
      map: map,
      spawn: _spawn,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      story: defaultStory,
      shops: defaultShops,
      combat: defaultCombat,
      random: random,
    );
    for (var i = 0; i < 4; i++) {
      server.objects.place(ObjectId.tree, TileCoord(30 + i, 34, 0));
      server.npcs.spawn(
        NpcSpawn(npc: NpcId.chicken, tile: TileCoord(30 + i, 30, 0), radius: 2),
      );
    }
    return server;
  }

  static Bot _join(ScapeServer server, String name, int reach, Random random) {
    final link = LoopbackLink();
    server.host.accept(link.host);
    return Bot(
      name: name,
      session: GameSession(
        transport: link.client,
        skills: defaultSkills,
        items: defaultItems,
        story: defaultStory,
      ),
      home: _spawn,
      reach: reach,
      random: Random(random.nextInt(1 << 30)),
    );
  }

  final ScapeServer server;
  final ServerProbe probe;
  final List<Bot> bots;
  final List<String> _problems;
  final int seed;

  /// Runs [ticks] ticks. Each bot acts, the server ticks, and then each bot
  /// reads what it was sent.
  Future<void> step(int ticks) async {
    for (var i = 0; i < ticks; i++) {
      for (final bot in bots) {
        bot.act();
      }
      await probe.step(1);
      for (final bot in bots) {
        bot.session.advance();
      }
    }
  }

  BotReport report() => BotReport(
    machine: currentMachine(),
    bots: bots.length,
    joined: bots.where((bot) => bot.isIn).length,
    seed: seed,
    tickTimes: probe.tickTimes,
    problems: [
      ..._problems,
      for (final bot in bots)
        if (bot.session.refusal case final why?)
          '${bot.name} was sent away: $why',
    ],
  );

  Future<void> dispose() async {
    for (final bot in bots) {
      await bot.session.dispose();
    }
    await server.dispose();
  }
}
