import 'dart:convert';
import 'dart:io';

/// A double as JSON can hold it. JSON has no NaN or infinity, so those travel
/// as the strings "NaN", "Infinity" and "-Infinity", which `Fixture.Number`
/// reads back.
Object number(double value) => value.isFinite ? value : value.toString();

/// An int JSON can carry exactly. A reader hands back a double past 2^53, so
/// those travel as strings.
Object big(int value) => value > 9007199254740992 || value < -9007199254740992
    ? value.toString()
    : value;

String b64(List<int> bytes) => base64Encode(bytes);

/// Bytes as lower-case hex, the way a fixture holds an encoded value.
String hex(List<int> bytes) =>
    bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();

/// A decoded JSON value with its doubles and big ints written the way a
/// fixture holds them.
Object? tree(Object? value) => switch (value) {
  final double d => number(d),
  final int i => big(i),
  final Map<Object?, Object?> m => {
    for (final e in m.entries) '${e.key}': tree(e.value),
  },
  final List<Object?> l => [for (final e in l) tree(e)],
  _ => value,
};

/// What a call gave, or that it threw. The kind of error is not compared:
/// Dart's `RangeError`, `FormatException`, `StateError` and the like have no one
/// .NET twin, and a refusal is what the callers see.
Map<String, Object?> attempt(Object? Function() body) {
  try {
    return {'value': body()};
  } on Object {
    return {'throws': true};
  }
}

/// Writes `dotnet/fixtures/<package>/<name>.json` under [root].
///
/// The file is stamped with the commit that last changed the package, so a
/// fixture says which Dart it came from and only changes when that Dart does.
/// One case per line keeps a regeneration diff readable.
void writeFixture({
  required String root,
  required String package,
  required String name,
  required List<Map<String, Object?>> cases,
}) {
  final body = cases.map((c) => '    ${jsonEncode(c)}').join(',\n');
  final file = File('$root/dotnet/fixtures/$package/$name.json');
  file.parent.createSync(recursive: true);
  file.writeAsStringSync(
    '{\n'
    '  "package": ${jsonEncode(package)},\n'
    '  "sourceCommit": ${jsonEncode(_commitOf(root, package))},\n'
    '  "cases": [\n$body\n  ]\n'
    '}\n',
  );
}

String _commitOf(String root, String package) {
  final path = 'packages/$package';
  String git(List<String> args) {
    final result = Process.runSync('git', args, workingDirectory: root);
    if (result.exitCode != 0) {
      throw StateError('git ${args.join(' ')} failed: ${result.stderr}');
    }
    return (result.stdout as String).trim();
  }

  final commit = git(['log', '-1', '--format=%H', '--', path]);
  final changed = git(['status', '--porcelain', '--', path]);
  return changed.isEmpty ? commit : '$commit+dirty';
}
