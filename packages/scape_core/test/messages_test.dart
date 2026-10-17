import 'dart:math';
import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

ClientMessage understoodClient(Uint8List bytes) {
  final decoded = decodeClientMessage(bytes);
  return switch (decoded) {
    Understood(:final message) => message,
    Unreadable(:final why) => fail('unreadable: $why'),
  };
}

ServerMessage understoodServer(Uint8List bytes) {
  final decoded = decodeServerMessage(bytes);
  return switch (decoded) {
    Understood(:final message) => message,
    Unreadable(:final why) => fail('unreadable: $why'),
  };
}

BadMessage? whyClient(List<int> bytes) =>
    switch (decodeClientMessage(Uint8List.fromList(bytes))) {
      Unreadable(:final why) => why,
      Understood() => null,
    };

BadMessage? whyServer(List<int> bytes) =>
    switch (decodeServerMessage(Uint8List.fromList(bytes))) {
      Unreadable(:final why) => why,
      Understood() => null,
    };

void main() {
  group('names and text', () {
    test('accepts letters, digits and inner spaces up to twelve', () {
      for (final name in ['a', 'Ana', 'Zed 99', 'a_b-c', 'abcdefghijkl']) {
        expect(isValidName(name), isTrue, reason: name);
      }
    });

    test('refuses an empty, long, padded or odd name', () {
      for (final name in [
        '',
        'abcdefghijklm',
        ' ana',
        'ana ',
        '_ana',
        'aé',
        'a\nb',
      ]) {
        expect(isValidName(name), isFalse, reason: name);
      }
    });

    test('names that differ only in case share a key', () {
      expect(nameKey('Ana'), nameKey('aNA'));
    });

    test('text may be anything printable up to 240 bytes', () {
      expect(isValidText('hello é world'), isTrue);
      expect(isValidText('x' * 240), isTrue);
      expect(isValidText('x' * 241), isFalse);
      expect(isValidText('é' * 121), isFalse);
      expect(isValidText(''), isFalse);
      expect(isValidText('a\tb'), isFalse);
      expect(isValidText('a\u007fb'), isFalse);
    });
  });

  group('messages from a client', () {
    test('a login round-trips', () {
      final back = understoodClient(encodeClientMessage(Login('Ana')));
      expect(back, isA<Login>().having((m) => m.name, 'name', 'Ana'));
    });

    test('a walk round-trips with every tile field', () {
      final target = TileCoord(worldSize - 1, 12345, planeCount - 1);
      final back = understoodClient(encodeClientMessage(Walk(target)));
      expect(back, isA<Walk>().having((m) => m.target, 'target', target));
    });

    test('an interaction round-trips', () {
      final bytes = encodeClientMessage(
        Interact(kind: 255, id: 0xFFFFFFFF, option: 7),
      );
      expect(
        understoodClient(bytes),
        isA<Interact>()
            .having((m) => m.kind, 'kind', 255)
            .having((m) => m.id, 'id', 0xFFFFFFFF)
            .having((m) => m.option, 'option', 7),
      );
    });

    test('a chat line round-trips with its accents', () {
      final back = understoodClient(encodeClientMessage(Chat('café ☃')));
      expect(back, isA<Chat>().having((m) => m.text, 'text', 'café ☃'));
    });

    test('a message cannot be built from values that are out of range', () {
      expect(() => Login('no way too long'), throwsArgumentError);
      expect(() => Chat(''), throwsArgumentError);
      expect(() => Interact(kind: 256, id: 0, option: 0), throwsArgumentError);
      expect(() => Interact(kind: 0, id: -1, option: 0), throwsArgumentError);
      expect(() => Interact(kind: 0, id: 0, option: 256), throwsArgumentError);
      expect(
        () => Interact(kind: 0, id: 0x100000000, option: 0),
        throwsArgumentError,
      );
    });

    test('says why bytes are unreadable', () {
      expect(whyClient([]), BadMessage.empty);
      expect(whyClient([99, 1, 2]), BadMessage.unknownTag);
      expect(whyClient([0]), BadMessage.unknownTag);
      // A walk is six bytes.
      expect(whyClient([2, 0, 0]), BadMessage.malformed);
      expect(whyClient([2, 0, 0, 0, 0, 0, 0]), BadMessage.tooLarge);
      // A login whose length byte runs past the end.
      expect(whyClient([1, 5, 65]), BadMessage.malformed);
      // A tile outside the world, and a plane that does not exist.
      expect(whyClient([2, 0xFF, 0xFF, 0, 0, 0]), BadMessage.malformed);
      expect(whyClient([2, 0, 0, 0, 0, planeCount]), BadMessage.malformed);
      // A login name that is not a name.
      expect(whyClient([1, 2, 0x20, 0x20]), BadMessage.malformed);
      // A chat line that is not UTF-8, and one with a control character.
      expect(whyClient([4, 2, 0xC3, 0x28]), BadMessage.malformed);
      expect(whyClient([4, 1, 0x07]), BadMessage.malformed);
    });

    test('a message over its own cap is too large', () {
      final chat = Uint8List(2 + 241)
        ..[0] = 4
        ..[1] = 241;
      expect(whyClient(chat), BadMessage.tooLarge);
      expect(whyClient([1, ...List.filled(40, 65)]), BadMessage.tooLarge);
    });

    test('no message from a client is over the stated limit', () {
      final biggest = encodeClientMessage(Chat('x' * 240));
      expect(biggest.length, lessThanOrEqualTo(clientMessageLimit));
    });
  });

  group('messages from the server', () {
    test('a login ok round-trips', () {
      final back = understoodServer(encodeServerMessage(LoginOk('ana')));
      expect(back, isA<LoginOk>().having((m) => m.name, 'name', 'ana'));
    });

    test('an avatar round-trips the largest id', () {
      final back = understoodServer(encodeServerMessage(Avatar(0xFFFFFFFF)));
      expect(
        back,
        isA<Avatar>().having((m) => m.networkId, 'networkId', 0xFFFFFFFF),
      );
      expect(() => Avatar(-1), throwsArgumentError);
      expect(() => Avatar(0x100000000), throwsArgumentError);
      expect(whyServer([4, 1, 2]), BadMessage.malformed);
    });

    test('private state keeps its bytes', () {
      final delta = Uint8List.fromList([0, 1, 2, 255]);
      final back = understoodServer(encodeServerMessage(PrivateState(delta)));
      expect(back, isA<PrivateState>().having((m) => m.delta, 'delta', delta));
    });

    test('a notice round-trips', () {
      final back = understoodServer(encodeServerMessage(Notice('Welcome')));
      expect(back, isA<Notice>().having((m) => m.text, 'text', 'Welcome'));
    });

    test('private state over its cap cannot be built or read', () {
      expect(
        () => PrivateState(Uint8List(maxDeltaBytes + 1)),
        throwsArgumentError,
      );
      expect(
        whyServer([2, ...List.filled(maxDeltaBytes + 1, 0)]),
        BadMessage.tooLarge,
      );
      expect(
        PrivateState(Uint8List(maxDeltaBytes)).delta,
        hasLength(maxDeltaBytes),
      );
    });

    test('says why bytes are unreadable', () {
      expect(whyServer([]), BadMessage.empty);
      expect(whyServer([77]), BadMessage.unknownTag);
      expect(whyServer([3, 9, 65]), BadMessage.malformed);
    });
  });

  group('a login refusal', () {
    test('is told to the client by its name', () {
      for (final refusal in LoginRefusal.values) {
        expect(LoginRefusal.fromWire(refusal.name), refusal);
      }
      expect(LoginRefusal.fromWire('something else'), isNull);
    });
  });

  test('random bytes never throw past either decoder', () {
    final random = Random(7);
    for (var i = 0; i < 20000; i++) {
      final length = random.nextInt(i.isEven ? 12 : 300);
      final bytes = Uint8List.fromList([
        for (var j = 0; j < length; j++) random.nextInt(256),
      ]);
      if (length > 0 && i % 3 == 0) bytes[0] = 1 + random.nextInt(7);
      decodeClientMessage(bytes);
      decodeServerMessage(bytes);
    }
  });
}
