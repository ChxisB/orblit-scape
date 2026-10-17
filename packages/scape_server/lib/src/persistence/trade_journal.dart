import 'dart:convert';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';

/// The bytes of a trade journal: the saved state of each player in the trade,
/// as they stand after the swap. Names are lower case, as the server keys
/// players.
Uint8List encodeJournal(Map<String, Uint8List> states) => Uint8List.fromList(
  utf8.encode(
    jsonEncode({
      for (final MapEntry(:key, :value) in states.entries)
        key: base64Encode(value),
    }),
  ),
);

/// Reads a journal. Throws [FormatException] for bytes that are not one.
Map<String, Uint8List> decodeJournal(Uint8List bytes) {
  final Object? json;
  try {
    json = jsonDecode(utf8.decode(bytes));
  } on FormatException {
    throw const FormatException('A trade journal is not JSON.');
  }
  if (json is! Map<String, dynamic> || json.length != 2) {
    throw const FormatException('A trade journal names two players.');
  }
  final states = <String, Uint8List>{};
  for (final MapEntry(:key, :value) in json.entries) {
    if (!isValidName(key) || key != key.toLowerCase() || value is! String) {
      throw FormatException('A trade journal has a bad entry for "$key".');
    }
    states[key] = base64Decode(value);
  }
  return states;
}
