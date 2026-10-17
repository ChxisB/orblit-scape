import 'dart:io';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

void main() {
  test('the version constant matches pubspec.yaml', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();
    expect(pubspec, contains('version: $scapeCoreVersion\n'));
  });
}
