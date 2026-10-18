import 'dart:async';
import 'dart:io';

/// Answers every connection with `alive` and closes it. It runs on the
/// server's own isolate, so a stalled tick stalls the answer, and a monitor
/// sees that as a dead server.
Future<ServerSocket> listenAlive({
  required String address,
  required int port,
}) async {
  final server = await ServerSocket.bind(address, port);
  server.listen((client) {
    client.write('alive\n');
    // close() ends only the write side, and a socket nobody reads keeps the
    // process alive after SIGTERM. A monitor that hangs up early must not
    // take the server down either, so the failure is dropped.
    client.flush().whenComplete(client.destroy).ignore();
  }, onError: (Object _) {});
  return server;
}
