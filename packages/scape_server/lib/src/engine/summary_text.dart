import 'package:orblit_net/orblit_net.dart';

String _ms(Duration d) => '${(d.inMicroseconds / 1000).toStringAsFixed(1)} ms';

/// A late tick as lines of text, the worst phase of the window first.
String describeLateTick(PhaseSummary summary) {
  final phases = summary.phases.entries.toList()
    ..sort((a, b) => b.value.max.compareTo(a.value.max));
  return [
    'late tick: average ${_ms(summary.tickAverage)}, '
        'max ${_ms(summary.tickMax)}, '
        'drift max ${_ms(summary.driftMax)}, '
        'over ${summary.ticks} ticks',
    for (final entry in phases)
      '  ${entry.key}: average ${_ms(entry.value.average)}, '
          'max ${_ms(entry.value.max)}',
  ].join('\n');
}
