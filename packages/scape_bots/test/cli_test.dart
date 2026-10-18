import 'dart:convert';
import 'dart:io';

import 'package:scape_bots/scape_bots.dart';
import 'package:test/test.dart';

void main() {
  late List<String> lines;
  late Map<String, String> saved;

  Future<int> run(List<String> args) =>
      runBots(args, out: lines.add, save: (path, text) => saved[path] = text);

  setUp(() {
    lines = [];
    saved = {};
  });

  test('runs, saves the result and exits 0', () async {
    final code = await run(['--bots', '3', '--ticks', '5', '--out', 'r.json']);

    expect(code, 0);
    final json = jsonDecode(saved['r.json']!) as Map<String, Object?>;
    expect(json['bots'], 3);
    expect(json['joined'], 3);
    expect(json['ticks'], 5);
    expect(lines.first, startsWith('3 bots, 5 ticks. p95 '));
    expect(lines.last, 'Result in r.json.');
  });

  test('prints the result and exits 1 when it cannot be saved', () async {
    final code = await runBots(
      ['--bots', '2', '--ticks', '3', '--out', 'r.json'],
      out: lines.add,
      save: (path, text) => throw const FileSystemException('disk full'),
    );

    expect(code, 1);
    expect(lines.first, contains('Could not save r.json'));
    expect(lines.first, contains('"bots": 2'));
    expect(lines.last, isNot(startsWith('Result in')));
  });

  test(
    'defaults to 50 bots and 100 ticks, written to bots-result.json',
    () async {
      expect(await run(const []), 0);

      final json =
          jsonDecode(saved['bots-result.json']!) as Map<String, Object?>;
      expect(json['bots'], 50);
      expect(json['ticks'], 100);
    },
  );

  for (final args in [
    ['--bots', '0'],
    ['--bots', '10000'],
    ['--bots', 'many'],
    ['--ticks', '-1'],
    ['--reach', '1001'],
    ['--bots'],
    ['--speed', '3'],
  ]) {
    test('exits 2 and saves nothing for ${args.join(' ')}', () async {
      expect(await run(args), 2);
      expect(saved, isEmpty);
      expect(lines.single, endsWith(botsUsage));
    });
  }
}
