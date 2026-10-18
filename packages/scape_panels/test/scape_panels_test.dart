import 'dart:io';

import 'package:scape_panels/scape_panels.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('the version matches the pubspec', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();

    expect(pubspec, contains('version: $scapePanelsVersion\n'));
  });
}
