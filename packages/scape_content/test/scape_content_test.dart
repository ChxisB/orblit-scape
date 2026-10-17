import 'dart:io';

import 'package:scape_content/scape_content.dart';
import 'package:test/test.dart';

void main() {
  test('the version constant matches pubspec.yaml', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();
    expect(pubspec, contains('version: $scapeContentVersion\n'));
  });
}
