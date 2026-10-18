import 'dart:convert';

import 'package:scape_core/scape_core.dart';

import 'report.dart';
import 'run.dart';

const String botsUsage = '''
Usage: dart run scape_bots:bots [options]
  --bots N    bots to log in, 1 to 9999, default 50
  --ticks N   ticks to run, default 100
  --seed N    seed for what the bots do, default 1
  --reach N   tiles from the spawn a bot may walk, default 12
  --out PATH  where to write the result, default bots-result.json''';

typedef _Options = ({int bots, int ticks, int seed, int reach, String out});

int _number(String flag, String? text, int least, int most) {
  final value = int.tryParse(text ?? '');
  if (value == null || value < least || value > most) {
    throw FormatException('$flag wants a whole number from $least to $most.');
  }
  return value;
}

_Options _parse(List<String> args) {
  final values = {
    '--bots': '50',
    '--ticks': '100',
    '--seed': '1',
    '--reach': '12',
    '--out': 'bots-result.json',
  };
  for (var i = 0; i < args.length; i += 2) {
    if (!values.containsKey(args[i]) || i + 1 >= args.length) {
      throw FormatException('${args[i]} is not an option with a value.');
    }
    values[args[i]] = args[i + 1];
  }
  return (
    bots: _number('--bots', values['--bots'], 1, 9999),
    ticks: _number('--ticks', values['--ticks'], 1, 1000000),
    seed: _number('--seed', values['--seed'], 0, 1 << 30),
    reach: _number('--reach', values['--reach'], 0, 1000),
    out: values['--out'] ?? '',
  );
}

/// Runs the `bots` command: starts a server, logs in bots, steps ticks and
/// saves the result as JSON. The exit code is 0 when every bot got in, the
/// server logged nothing and the p95 tick fit in the tick period, 1 when not,
/// and 2 for arguments it cannot read.
Future<int> runBots(
  List<String> args, {
  required void Function(String line) out,
  required void Function(String path, String text) save,
}) async {
  final _Options options;
  try {
    options = _parse(args);
  } on FormatException catch (error) {
    out('${error.message}\n$botsUsage');
    return 2;
  }
  final run = await BotRun.start(
    bots: options.bots,
    seed: options.seed,
    reach: options.reach,
  );
  try {
    await run.step(options.ticks);
    final report = run.report();
    save(
      options.out,
      '${const JsonEncoder.withIndent('  ').convert(report.toJson())}\n',
    );
    out(
      '${report.bots} bots, ${options.ticks} ticks. '
      'p95 ${formatMs(report.p95)}, max ${formatMs(report.slowest)}, '
      'period ${tickPeriod.inMilliseconds} ms.',
    );
    if (!report.healthy) {
      out(
        'Not healthy: ${report.joined} of ${report.bots} bots joined, '
        '${report.problems.length} problems.',
      );
      report.problems.take(5).forEach(out);
    }
    out('Result in ${options.out}.');
    return report.exitCode;
  } finally {
    await run.dispose();
  }
}
