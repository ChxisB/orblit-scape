import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

T roundTrip<T extends ClientMessage>(T message) {
  final decoded = decodeClientMessage(encodeClientMessage(message));
  return (decoded as Understood<ClientMessage>).message as T;
}

T heard<T extends ServerMessage>(T message) {
  final decoded = decodeServerMessage(encodeServerMessage(message));
  return (decoded as Understood<ServerMessage>).message as T;
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
  group('from a client', () {
    test('a whisper keeps who it is for and what it says', () {
      final back = roundTrip(Whisper(to: 'Bo', text: 'café ☃'));
      expect(back.to, 'Bo');
      expect(back.text, 'café ☃');
    });

    test('a list edit keeps the list, the direction and the name', () {
      for (final list in ListKind.values) {
        for (final add in [true, false]) {
          final back = roundTrip(EditList(list: list, add: add, name: 'Bo'));
          expect((back.list, back.add, back.name), (list, add, 'Bo'));
        }
      }
    });

    test('cannot be built from a bad name or text', () {
      expect(
        () => Whisper(to: 'no way too long', text: 'hi'),
        throwsArgumentError,
      );
      expect(() => Whisper(to: 'Bo', text: ''), throwsArgumentError);
      expect(() => Whisper(to: 'Bo', text: 'x' * 241), throwsArgumentError);
      expect(
        () => EditList(list: ListKind.friends, add: true, name: ' '),
        throwsArgumentError,
      );
    });

    test('says why a whisper or a list edit is unreadable', () {
      // The first text runs past the end.
      expect(whyClient([5, 9, 66]), BadMessage.malformed);
      // No second text.
      expect(whyClient([5, 2, 66, 111]), BadMessage.malformed);
      // Bytes left over after the second text.
      expect(whyClient([5, 1, 66, 1, 104, 0]), BadMessage.malformed);
      // A name that is not a name.
      expect(whyClient([5, 1, 0x20, 1, 104]), BadMessage.malformed);
      // Unused flag bits, and a length that does not match.
      expect(whyClient([6, 4, 1, 66]), BadMessage.malformed);
      expect(whyClient([6, 0, 3, 66]), BadMessage.malformed);
      expect(whyClient([6]), BadMessage.malformed);
    });

    test('a whisper at the text cap is let through and one over is not', () {
      final fits = encodeClientMessage(
        Whisper(to: 'abcdefghijkl', text: 'x' * maxTextBytes),
      );
      expect(fits.length, lessThanOrEqualTo(clientMessageLimit));
      expect(whyClient([...fits, 0]), BadMessage.tooLarge);
    });
  });

  group('from the server', () {
    test('said keeps the speaker and the line', () {
      final back = heard(Said(speaker: 0xFFFFFFFF, text: 'hello there'));
      expect((back.speaker, back.text), (0xFFFFFFFF, 'hello there'));
    });

    test('whispered keeps who from and the line', () {
      final back = heard(Whispered(from: 'Ana', text: 'psst'));
      expect((back.from, back.text), ('Ana', 'psst'));
    });

    test('social lists keep friends, who is online and the ignored', () {
      final back = heard(
        SocialLists(
          friends: [(name: 'Bo', online: true), (name: 'Cy', online: false)],
          ignored: ['Dee'],
        ),
      );
      expect(back.friends, [
        (name: 'Bo', online: true),
        (name: 'Cy', online: false),
      ]);
      expect(back.ignored, ['Dee']);
    });

    test('empty social lists round-trip', () {
      final back = heard(SocialLists(friends: [], ignored: []));
      expect(back.friends, isEmpty);
      expect(back.ignored, isEmpty);
    });

    test('full social lists fit the stated cap', () {
      final names = [
        for (var i = 0; i < maxListEntries; i++)
          'abcdefghi${i % 10}${i ~/ 10}x'.substring(0, 12),
      ];
      final lists = SocialLists(
        friends: [for (final n in names) (name: n, online: true)],
        ignored: names,
      );
      expect(heard(lists).friends, hasLength(maxListEntries));
    });

    test('cannot be built over the list cap or from a bad name', () {
      expect(
        () => SocialLists(
          friends: [
            for (var i = 0; i <= maxListEntries; i++)
              (name: 'a', online: false),
          ],
          ignored: [],
        ),
        throwsArgumentError,
      );
      expect(
        () => SocialLists(friends: [], ignored: ['_x']),
        throwsArgumentError,
      );
      expect(() => Said(speaker: -1, text: 'x'), throwsArgumentError);
    });

    test('says why bytes are unreadable', () {
      expect(whyServer([5, 1, 0, 0]), BadMessage.malformed);
      expect(whyServer([6, 1, 66]), BadMessage.malformed);
      // The lists run out part way.
      expect(whyServer([7, 1, 2, 66]), BadMessage.malformed);
      expect(whyServer([7, 0]), BadMessage.malformed);
      expect(whyServer([7, 0, 1]), BadMessage.malformed);
      expect(whyServer([7, 0, 0, 0]), BadMessage.malformed);
    });
  });
}
