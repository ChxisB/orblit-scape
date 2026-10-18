import 'dart:convert';
import 'dart:io';

import 'package:orblit_net/orblit_net.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

void main() {
  test('the health line names players, regions and the tick', () {
    const summary = PhaseSummary(
      ticks: 100,
      tickAverage: Duration(microseconds: 2150),
      tickMax: Duration(microseconds: 7400),
      driftAverage: Duration.zero,
      driftMax: Duration.zero,
      phases: {},
      bytesSent: {},
    );

    expect(
      healthLine(players: 3, regions: 4, summary: summary),
      'Health: 3 players, 4 regions, tick average 2.1 ms, max 7.4 ms '
      'over 100 ticks',
    );
  });

  test('the alive port answers alive to each connection', () async {
    final server = await listenAlive(address: '127.0.0.1', port: 0);
    addTearDown(server.close);

    for (var i = 0; i < 2; i++) {
      final socket = await Socket.connect('127.0.0.1', server.port);
      final reply = await utf8.decodeStream(socket);
      expect(reply, 'alive\n');
      socket.destroy();
    }
  });

  test('a monitor that hangs up at once does not stop it answering', () async {
    final server = await listenAlive(address: '127.0.0.1', port: 0);
    addTearDown(server.close);

    (await Socket.connect('127.0.0.1', server.port)).destroy();
    await Future<void>.delayed(const Duration(milliseconds: 50));
    final socket = await Socket.connect('127.0.0.1', server.port);

    expect(await utf8.decodeStream(socket), 'alive\n');
  });
}
