import 'dart:io';

import 'package:scape_bots/scape_bots.dart';

Future<void> main(List<String> args) async {
  exit(
    await runBots(
      args,
      out: stdout.writeln,
      save: (path, text) => File(path).writeAsStringSync(text),
    ),
  );
}
