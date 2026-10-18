import 'package:scape_core/scape_core.dart' show isValidName, nameKey;

import 'features/admin/admin.dart' show Role;

/// How much the server binary writes to its log. `warn` keeps what went
/// wrong. `info` adds the start line and the health line each minute.
enum LogLevel { warn, info }

final RegExp _worldName = RegExp(r'^[a-z0-9][a-z0-9_-]{0,31}$');

/// What the server binary reads from its environment. Each value has a
/// default. A value that does not parse throws a [FormatException] that names
/// the variable, so a typo stops the server before it listens. An empty value
/// counts as unset. Only [ServerConfig.fromEnvironment] builds one, so every
/// instance has passed the checks.
final class ServerConfig {
  const ServerConfig._({
    required this.port,
    required this.address,
    required this.data,
    required this.world,
    required this.healthPort,
    required this.logLevel,
    required this.roles,
  });

  factory ServerConfig.fromEnvironment(Map<String, String> env) {
    String? value(String name) {
      final text = env[name];
      return text == null || text.isEmpty ? null : text;
    }

    final port = _port(value('SCAPE_PORT'), 'SCAPE_PORT', 7600, least: 1);
    final healthPort = _port(
      value('SCAPE_HEALTH_PORT'),
      'SCAPE_HEALTH_PORT',
      7601,
      least: 0,
    );
    if (healthPort == port) {
      throw FormatException(
        'SCAPE_HEALTH_PORT and SCAPE_PORT are both $port. They must differ',
      );
    }
    return ServerConfig._(
      port: port,
      address: value('SCAPE_ADDRESS') ?? '127.0.0.1',
      data: value('SCAPE_DATA') ?? 'scape-saves',
      world: _world(value('SCAPE_WORLD')),
      healthPort: healthPort,
      logLevel: _level(value('SCAPE_LOG_LEVEL')),
      roles: _roles(value('SCAPE_ADMINS')),
    );
  }

  /// The port players connect to.
  final int port;
  final String address;

  /// The folder that holds every world's saves.
  final String data;

  /// The name of this world. Its saves sit in a folder of that name in
  /// [data].
  final String world;

  /// The port that answers `alive`, or 0 for none.
  final int healthPort;
  final LogLevel logLevel;

  /// Who may run commands, from `SCAPE_ADMINS`, for example
  /// `ana=admin,bo=moderator`.
  final Map<String, Role> roles;

  String get saves => '$data/$world';
}

int _port(String? text, String name, int fallback, {required int least}) {
  if (text == null) return fallback;
  final port = int.tryParse(text);
  if (port == null || port < least || port > 65535) {
    throw FormatException(
      '$name wants a whole number from $least to 65535, '
      'not "$text"',
    );
  }
  return port;
}

String _world(String? text) {
  if (text == null) return 'scape';
  if (!_worldName.hasMatch(text)) {
    throw FormatException(
      'SCAPE_WORLD wants 1 to 32 lower-case letters, digits, "-" or "_", '
      'not "$text"',
    );
  }
  return text;
}

LogLevel _level(String? text) {
  if (text == null) return LogLevel.info;
  final level = LogLevel.values.asNameMap()[text];
  if (level == null) {
    throw FormatException('SCAPE_LOG_LEVEL wants warn or info, not "$text"');
  }
  return level;
}

Map<String, Role> _roles(String? text) {
  final roles = <String, Role>{};
  final pairs = (text ?? '').split(',').map((p) => p.trim());
  for (final pair in pairs.where((p) => p.isNotEmpty)) {
    final parts = pair.split('=').map((p) => p.trim()).toList();
    final role = parts.length == 2 ? Role.values.asNameMap()[parts[1]] : null;
    if (role == null || !isValidName(parts[0])) {
      throw FormatException('SCAPE_ADMINS has "$pair"');
    }
    // A player is known by the lower-case key, so a role under any other
    // spelling would never apply.
    if (roles.containsKey(nameKey(parts[0]))) {
      throw FormatException('SCAPE_ADMINS names ${parts[0]} twice');
    }
    roles[nameKey(parts[0])] = role;
  }
  return roles;
}
