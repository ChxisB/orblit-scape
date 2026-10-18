import 'dart:io';

import 'package:scape_core/scape_core.dart';

/// Where a run happened. A tick time means nothing without it.
typedef Machine = ({String os, String osVersion, int processors, String dart});

/// The machine this process runs on. F10: a report names the real machine.
Machine currentMachine() => (
  os: Platform.operatingSystem,
  osVersion: Platform.operatingSystemVersion,
  processors: Platform.numberOfProcessors,
  dart: Platform.version.split(' ').first,
);

/// The tick time [p] of the way up [sorted], from 0 to 1, by nearest rank.
/// Zero when there are no ticks.
Duration percentile(List<Duration> sorted, double p) {
  if (sorted.isEmpty) return Duration.zero;
  final rank = (p * sorted.length).ceil().clamp(1, sorted.length);
  return sorted[rank - 1];
}

double _ms(Duration time) => time.inMicroseconds / 1000;

/// A tick time as text, in milliseconds.
String formatMs(Duration time) => '${_ms(time).toStringAsFixed(3)} ms';

/// What one run of bots found. The tick times are the server's own, taken
/// round the server's tick, so they leave out what the bots spent.
final class BotReport {
  BotReport({
    required this.machine,
    required this.bots,
    required this.joined,
    required this.seed,
    required List<Duration> tickTimes,
    required List<String> problems,
  }) : tickTimes = List.unmodifiable(tickTimes),
       _sorted = List.unmodifiable([...tickTimes]..sort()),
       problems = List.unmodifiable(problems);

  final Machine machine;

  final int bots;

  /// How many of the bots were in the world at the end.
  final int joined;
  final int seed;
  final List<Duration> tickTimes;
  final List<Duration> _sorted;

  /// Lines the server logged, and bots that were sent away.
  final List<String> problems;

  Duration get p95 => percentile(_sorted, 0.95);

  Duration get slowest => _sorted.isEmpty ? Duration.zero : _sorted.last;

  /// Whether every bot got in, nothing went wrong, and the p95 tick fit in
  /// the tick period.
  bool get healthy => joined == bots && problems.isEmpty && p95 <= tickPeriod;

  /// 0 when [healthy], 1 when not.
  int get exitCode => healthy ? 0 : 1;

  Map<String, Object?> toJson() {
    final total = tickTimes.fold(Duration.zero, (sum, time) => sum + time);
    return {
      'machine': {
        'os': machine.os,
        'osVersion': machine.osVersion,
        'processors': machine.processors,
        'dart': machine.dart,
      },
      'transport': 'loopback',
      'without': ['storage', 'accounts', 'recipes', 'spells', 'prayers'],
      'bots': bots,
      'joined': joined,
      'seed': seed,
      'ticks': tickTimes.length,
      'tickMs': {
        'average': _ms(
          tickTimes.isEmpty ? Duration.zero : total ~/ tickTimes.length,
        ),
        'p50': _ms(percentile(_sorted, 0.5)),
        'p95': _ms(percentile(_sorted, 0.95)),
        'max': _ms(slowest),
      },
      'periodMs': tickPeriod.inMilliseconds,
      'healthy': healthy,
      'problems': problems,
    };
  }
}
