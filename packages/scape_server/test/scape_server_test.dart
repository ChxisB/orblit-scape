import 'dart:io';

import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

void main() {
  test('the version constant matches pubspec.yaml', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();
    expect(pubspec, contains('version: $scapeServerVersion\n'));
  });
}
