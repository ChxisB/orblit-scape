import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

void main() {
  test('every setting has a default', () {
    final config = ServerConfig.fromEnvironment(const {});

    expect(config.port, 7600);
    expect(config.address, '127.0.0.1');
    expect(config.data, 'scape-saves');
    expect(config.world, 'scape');
    expect(config.saves, 'scape-saves/scape');
    expect(config.healthPort, 7601);
    expect(config.logLevel, LogLevel.info);
    expect(config.roles, isEmpty);
  });

  test('reads each setting from the environment', () {
    final config = ServerConfig.fromEnvironment(const {
      'SCAPE_PORT': '8000',
      'SCAPE_ADDRESS': '0.0.0.0',
      'SCAPE_DATA': '/var/lib/scape',
      'SCAPE_WORLD': 'island-2',
      'SCAPE_HEALTH_PORT': '0',
      'SCAPE_LOG_LEVEL': 'warn',
      'SCAPE_ADMINS': 'ana=admin,bo=moderator',
    });

    expect(config.port, 8000);
    expect(config.address, '0.0.0.0');
    expect(config.saves, '/var/lib/scape/island-2');
    expect(config.healthPort, 0);
    expect(config.logLevel, LogLevel.warn);
    expect(config.roles, {'ana': Role.admin, 'bo': Role.moderator});
  });

  test('an empty value counts as unset', () {
    final config = ServerConfig.fromEnvironment(const {
      'SCAPE_PORT': '',
      'SCAPE_WORLD': '',
      'SCAPE_ADMINS': '',
    });

    expect(config.port, 7600);
    expect(config.world, 'scape');
    expect(config.roles, isEmpty);
  });

  for (final (name, value) in [
    ('SCAPE_PORT', '0'),
    ('SCAPE_PORT', '65536'),
    ('SCAPE_PORT', 'seven'),
    ('SCAPE_HEALTH_PORT', '-1'),
    ('SCAPE_HEALTH_PORT', '70000'),
    ('SCAPE_WORLD', 'Island'),
    ('SCAPE_WORLD', '../etc'),
    ('SCAPE_WORLD', '-lead'),
    ('SCAPE_WORLD', 'a' * 33),
    ('SCAPE_LOG_LEVEL', 'debug'),
    ('SCAPE_ADMINS', 'ana'),
    ('SCAPE_ADMINS', 'ana=owner'),
    ('SCAPE_ADMINS', 'ana=admin=x'),
    ('SCAPE_ADMINS', 'ana!=admin'),
    ('SCAPE_ADMINS', 'abcdefghijklm=admin'),
    ('SCAPE_ADMINS', 'ana=admin,Ana=moderator'),
    ('SCAPE_PORT', '7601'),
    ('SCAPE_HEALTH_PORT', '7600'),
  ]) {
    test('stops startup for $name=$value', () {
      expect(
        () => ServerConfig.fromEnvironment({name: value}),
        throwsA(
          isA<FormatException>().having(
            (error) => error.message,
            'message',
            contains(name),
          ),
        ),
      );
    });
  }

  test('keeps a world name of 32 characters and the top port', () {
    final config = ServerConfig.fromEnvironment({
      'SCAPE_WORLD': 'a' * 32,
      'SCAPE_PORT': '65535',
      'SCAPE_HEALTH_PORT': '65534',
    });

    expect(config.world, hasLength(32));
    expect(config.port, 65535);
    expect(config.healthPort, 65534);
  });

  test('reads admin names with spaces and capitals as lower-case keys', () {
    final config = ServerConfig.fromEnvironment({
      'SCAPE_ADMINS': 'Ana = admin, bo=moderator',
    });

    expect(config.roles.keys, ['ana', 'bo']);
    expect(config.roles['ana'], Role.admin);
  });
}
