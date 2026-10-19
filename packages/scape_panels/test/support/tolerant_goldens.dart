import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';

/// The share of pixels a golden may differ in, as a percentage.
///
/// The goldens were drawn on Apple Silicon. An x86 Linux runner anti-aliases
/// the same shapes a little differently and differs in 0.25% to 0.92% of the
/// pixels. A moved or missing panel changes far more than this.
const double goldenTolerance = 1.5;

/// A file comparator that passes a golden within [goldenTolerance].
final class TolerantComparator extends LocalFileComparator {
  TolerantComparator(super.testFile);

  /// The comparator in use, replaced by one that reads the same folder.
  factory TolerantComparator.beside(GoldenFileComparator current) =>
      TolerantComparator(
        (current as LocalFileComparator).basedir.resolve('panels_test.dart'),
      );

  @override
  Future<bool> compare(Uint8List imageBytes, Uri golden) async {
    final result = await GoldenFileComparator.compareLists(
      imageBytes,
      await getGoldenBytes(golden),
    );
    if (result.passed || result.diffPercent <= goldenTolerance / 100) {
      return true;
    }
    throw FlutterError(await generateFailureOutput(result, golden, basedir));
  }
}
