import 'dart:async';
import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// Flat open ground until the starter island exists.
TileMap _openWorld() {
  final map = TileMap();
  for (var y = 0; y < 2; y++) {
    for (var x = 0; x < 2; x++) {
      map.addRegion(FlagRegion.open(RegionKey(x, y, 0)));
    }
  }
  return map;
}

/// The server with its saves on disk and every part of the world wired in.
ScapeServer _serverFor(ServerConfig config, TileMap map) {
  final clock = TickClock(period: tickPeriod);
  final history = HistoryStore.files(config.saves);
  final queue = SaveQueue.spawn(
    history,
    clock: DateTime.now,
    onFailure: (key, error) => stderr.writeln('Save of $key failed: $error'),
  );
  return ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
    map: map,
    spawn: TileCoord(32, 32, 0),
    skills: defaultSkills,
    items: defaultItems,
    objects: defaultObjects,
    nodes: defaultNodes,
    npcs: defaultNpcs,
    story: defaultStory,
    combat: defaultCombat,
    shops: defaultShops,
    recipes: defaultRecipes,
    spells: defaultSpells,
    prayers: defaultPrayers,
    storage: Storage(
      queue: queue,
      hasher: const Argon2idHasher(),
      history: history,
    ),
    roles: config.roles,
  );
}

/// How often the health line is logged.
const _healthEvery = Duration(minutes: 1);

/// How many ticks the health line averages over, about a minute of them.
const _healthTicks = 100;

/// Reads its settings from the environment: `SCAPE_PORT`, `SCAPE_ADDRESS`,
/// `SCAPE_DATA`, `SCAPE_WORLD`, `SCAPE_HEALTH_PORT`, `SCAPE_LOG_LEVEL` and
/// `SCAPE_ADMINS`. See [ServerConfig]. It listens on the local machine only
/// unless an address says otherwise.
Future<void> main() async {
  final ServerConfig config;
  try {
    config = ServerConfig.fromEnvironment(Platform.environment);
  } on FormatException catch (error) {
    stderr.writeln('scape_server: ${error.message}');
    exit(78);
  }
  final info = config.logLevel == LogLevel.info;
  final map = _openWorld();
  final server = _serverFor(config, map);
  final sockets = await server.listen(
    port: config.port,
    address: config.address,
  );
  final alive = config.healthPort == 0
      ? null
      : await listenAlive(address: config.address, port: config.healthPort);
  final health = info
      ? Timer.periodic(
          _healthEvery,
          (_) => stdout.writeln(
            healthLine(
              players: server.movement.players.length,
              regions: map.regionCount,
              summary: server.host.summary(last: _healthTicks),
            ),
          ),
        )
      : null;
  // A service manager stops a server with SIGTERM, and a stop that skips the
  // save loses what players did since their last autosave.
  final stops = [
    for (final signal in [ProcessSignal.sigint, ProcessSignal.sigterm])
      signal.watch().listen((_) => server.loop.stop()),
  ];
  if (info) {
    stdout.writeln(
      'scape_server: world ${config.world}, ${sockets.url}, '
      'a tick every ${tickPeriod.inMilliseconds} ms',
    );
  }
  await server.loop.run();
  // A watched signal keeps the process alive, so stop watching to let it end.
  for (final stop in stops) {
    await stop.cancel();
  }
  health?.cancel();
  await alive?.close();
  await sockets.close();
  await server.dispose();
}
