import 'dart:async';
import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
import 'package:orblit_save/orblit_save.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';

/// The starter island. A server whose data files disagree with each other
/// stops with 78, since players would meet the fault one at a time and a
/// restart cannot fix it.
WorldPlan _worldOrExit() {
  final world = starterIsland();
  final problems = checkContent(
    items: defaultItems,
    objects: defaultObjects,
    nodes: defaultNodes,
    npcs: defaultNpcs,
    recipes: defaultRecipes,
    story: defaultStory,
    world: world,
  );
  if (problems.isNotEmpty) {
    _refuse('the game data disagrees with itself:\n  ${problems.join('\n  ')}');
  }
  return world;
}

/// The server with its saves on disk and every part of the world wired in.
ScapeServer _serverFor(ServerConfig config, WorldPlan world) {
  final clock = TickClock(period: tickPeriod);
  final history = HistoryStore.files(config.saves);
  final queue = SaveQueue.spawn(
    history,
    clock: DateTime.now,
    onFailure: (key, error) => stderr.writeln('Save of $key failed: $error'),
  );
  final server = ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
    map: world.map,
    spawn: world.spawn,
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
  return server..stock(objects: world.objects, npcs: world.npcs);
}

/// How often the health line is logged.
const _healthEvery = Duration(minutes: 1);

/// How many ticks the health line averages over, the ticks in one interval.
final _healthTicks = _healthEvery.inMicroseconds ~/ tickPeriod.inMicroseconds;

/// Stops with 78, the code for a bad setting. The unit does not restart a
/// server that stopped with it, since a restart cannot fix a setting.
Never _refuse(String message) {
  stderr.writeln('scape_server: $message');
  exit(78);
}

ServerConfig _configOrExit() {
  try {
    return ServerConfig.fromEnvironment(Platform.environment);
  } on FormatException catch (error) {
    _refuse(error.message);
  }
}

/// A folder the server cannot write lets players register and then lose
/// their work, so a server that cannot write it does not start.
void _requireWritable(ServerConfig config) {
  try {
    Directory(config.saves).createSync(recursive: true);
    File('${config.saves}/.writable')
      ..writeAsStringSync('')
      ..deleteSync();
  } on FileSystemException catch (error) {
    _refuse('SCAPE_DATA ${config.data} is not writable: ${error.message}');
  }
}

Future<T> _bind<T>(String name, Future<T> Function() open) async {
  try {
    return await open();
  } on SocketException catch (error) {
    _refuse('cannot listen for $name: $error');
  }
}

Timer _logHealth(ScapeServer server, TileMap map) => Timer.periodic(
  _healthEvery,
  (_) => stdout.writeln(
    healthLine(
      players: server.movement.players.length,
      regions: map.regionCount,
      summary: server.host.summary(last: _healthTicks),
    ),
  ),
);

/// A service manager stops a server with SIGTERM, and a stop that skips the
/// save loses what players did since their last autosave.
List<StreamSubscription<ProcessSignal>> _stopOnSignals(ScapeServer server) => [
  for (final signal in [ProcessSignal.sigint, ProcessSignal.sigterm])
    signal.watch().listen((_) => server.loop.stop()),
];

/// Reads its settings from the environment: `SCAPE_PORT`, `SCAPE_ADDRESS`,
/// `SCAPE_DATA`, `SCAPE_WORLD`, `SCAPE_HEALTH_PORT`, `SCAPE_LOG_LEVEL` and
/// `SCAPE_ADMINS`. See [ServerConfig]. It listens on the local machine only
/// unless an address says otherwise. Earlier versions took the port, the
/// address and the save folder as arguments, so an argument stops it with 64
/// and not a quiet start on the wrong settings.
Future<void> main(List<String> args) async {
  if (args.isNotEmpty) {
    stderr.writeln('scape_server takes no arguments. Set SCAPE_PORT and the');
    stderr.writeln('other variables instead. See deploy/server.env.example.');
    exit(64);
  }
  final config = _configOrExit();
  final world = _worldOrExit();
  _requireWritable(config);
  final info = config.logLevel == LogLevel.info;
  final server = _serverFor(config, world);
  final sockets = await _bind(
    'SCAPE_PORT ${config.port} on SCAPE_ADDRESS ${config.address}',
    () => server.listen(port: config.port, address: config.address),
  );
  final alive = config.healthPort == 0
      ? null
      : await _bind(
          'SCAPE_HEALTH_PORT ${config.healthPort}',
          () => listenAlive(address: config.address, port: config.healthPort),
        );
  final health = info ? _logHealth(server, world.map) : null;
  final stops = _stopOnSignals(server);
  if (info) {
    stdout.writeln(
      'scape_server: world ${config.world}, ${sockets.url}, '
      'a tick every ${tickPeriod.inMilliseconds} ms',
    );
  }
  try {
    await server.loop.run();
  } finally {
    // A tick that throws must still save every player before the process ends.
    health?.cancel();
    await alive?.close();
    await sockets.close();
    await server.dispose();
    // A watched signal keeps the process alive. The watchers go last so that
    // a second signal during the save does not kill the process mid-save.
    for (final stop in stops) {
      await stop.cancel();
    }
  }
}
