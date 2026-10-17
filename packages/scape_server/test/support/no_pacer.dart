import 'package:scape_server/scape_server.dart';

/// Never waits, so the test steps the loop by hand.
final class NoPacer implements Pacer {
  @override
  Future<void> untilNext() async {}
}
