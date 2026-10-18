import 'package:scape_bots/scape_bots.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

const Machine _machine = (
  os: 'testos',
  osVersion: '1',
  processors: 4,
  dart: '3.0.0',
);

List<Duration> _ms(Iterable<int> values) => [
  for (final value in values) Duration(milliseconds: value),
];

BotReport _report({
  int bots = 2,
  int joined = 2,
  List<Duration>? ticks,
  List<String> problems = const [],
}) => BotReport(
  machine: _machine,
  bots: bots,
  joined: joined,
  seed: 7,
  tickTimes: ticks ?? _ms([1, 2, 3]),
  problems: problems,
);

void main() {
  group('percentile', () {
    test('takes the nearest rank', () {
      final sorted = _ms(List.generate(20, (i) => i + 1));

      expect(percentile(sorted, 0.95), const Duration(milliseconds: 19));
      expect(percentile(sorted, 0.5), const Duration(milliseconds: 10));
      expect(percentile(sorted, 1), const Duration(milliseconds: 20));
    });

    test('rounds up to the next rank', () {
      expect(percentile(_ms([1, 2, 3]), 0.95), const Duration(milliseconds: 3));
    });

    test('is zero when there are no ticks', () {
      expect(percentile(const [], 0.95), Duration.zero);
    });
  });

  group('BotReport', () {
    test('writes the machine, the bot count and the p95', () {
      final json = _report(
        ticks: _ms(List.generate(20, (i) => 20 - i)),
      ).toJson();

      expect(json['machine'], {
        'os': 'testos',
        'osVersion': '1',
        'processors': 4,
        'dart': '3.0.0',
      });
      expect(json['bots'], 2);
      expect(json['seed'], 7);
      expect(json['ticks'], 20);
      expect(json['tickMs'], {
        'average': 10.5,
        'p50': 10.0,
        'p95': 19.0,
        'max': 20.0,
      });
    });

    test('is healthy when every bot is in and nothing went wrong', () {
      expect(_report().healthy, isTrue);
    });

    test('is not healthy when a bot did not get in', () {
      expect(_report(joined: 1).healthy, isFalse);
    });

    test('is not healthy when the server logged a problem', () {
      expect(_report(problems: ['late']).healthy, isFalse);
    });

    test('is not healthy when the p95 tick is over the period', () {
      final slow = [tickPeriod + const Duration(milliseconds: 1)];

      expect(_report(ticks: slow).healthy, isFalse);
    });

    test('exits 0 when healthy and 1 when not', () {
      expect(_report().exitCode, 0);
      expect(_report(joined: 1).exitCode, 1);
    });
  });
}
