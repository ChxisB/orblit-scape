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

/// The roles in `SCAPE_ADMINS`, written `ana=admin,bo=moderator`. Anything
/// else stops the server, so a typo never leaves a role unset.
Map<String, Role> _rolesOf(String? text) {
  final roles = <String, Role>{};
  for (final pair in (text ?? '').split(',').where((p) => p.isNotEmpty)) {
    final parts = pair.split('=');
    final role = parts.length == 2 ? Role.values.asNameMap()[parts[1]] : null;
    if (role == null) throw FormatException('SCAPE_ADMINS has "$pair"');
    roles[parts[0]] = role;
  }
  return roles;
}

/// `scape_server [port] [address] [saves]`. It listens on the local machine
/// only unless an address says otherwise, and keeps players in the `saves`
/// folder, `scape-saves` by default. `SCAPE_ADMINS` names who may run
/// commands.
Future<void> main(List<String> args) async {
  final clock = TickClock(period: tickPeriod);
  final history = HistoryStore.files(args.length > 2 ? args[2] : 'scape-saves');
  final queue = SaveQueue.spawn(
    history,
    clock: DateTime.now,
    onFailure: (key, error) => stderr.writeln('Save of $key failed: $error'),
  );
  final server = ScapeServer(
    clock: clock,
    pacer: ClockPacer(clock),
    log: stderr.writeln,
    map: _openWorld(),
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
    roles: _rolesOf(Platform.environment['SCAPE_ADMINS']),
  );
  final sockets = await server.listen(
    port: args.isEmpty ? 7600 : int.parse(args.first),
    address: args.length > 1 ? args[1] : '127.0.0.1',
  );
  // A service manager stops a server with SIGTERM, and a stop that skips the
  // save loses what players did since their last autosave.
  final stops = [
    for (final signal in [ProcessSignal.sigint, ProcessSignal.sigterm])
      signal.watch().listen((_) => server.loop.stop()),
  ];
  stdout.writeln(
    'scape_server: ${sockets.url}, a tick every ${tickPeriod.inMilliseconds} ms',
  );
  await server.loop.run();
  // A watched signal keeps the process alive, so stop watching to let it end.
  for (final stop in stops) {
    await stop.cancel();
  }
  await sockets.close();
  await server.dispose();
}
