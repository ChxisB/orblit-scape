import 'dart:io';

import 'package:scape_bots/scape_bots.dart';
import 'package:test/test.dart';

void main() {
  test('the version constant matches pubspec.yaml', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();
    expect(pubspec, contains('version: $scapeBotsVersion\n'));
  });
}
