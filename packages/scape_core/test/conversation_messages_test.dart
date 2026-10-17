import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

T heard<T extends ServerMessage>(T message) =>
    (decodeServerMessage(encodeServerMessage(message))
                as Understood<ServerMessage>)
            .message
        as T;

BadMessage? whyServer(List<int> bytes) =>
    switch (decodeServerMessage(Uint8List.fromList(bytes))) {
      Unreadable(:final why) => why,
      Understood() => null,
    };

BadMessage? whyClient(List<int> bytes) =>
    switch (decodeClientMessage(Uint8List.fromList(bytes))) {
      Unreadable(:final why) => why,
      Understood() => null,
    };

void main() {
  test('a line keeps its speaker and text', () {
    final back = heard(ConversationLine(speaker: 'Brindle', text: 'Hello.'));

    expect((back.speaker, back.text), ('Brindle', 'Hello.'));
  });

  test('choices keep their order', () {
    expect(heard(ConversationChoices(['Yes.', 'No.', 'Maybe.'])).options, [
      'Yes.',
      'No.',
      'Maybe.',
    ]);
  });

  test('a line and choices at their largest still fit their caps', () {
    final long = 'a' * maxTextBytes;

    expect(heard(ConversationLine(speaker: long, text: long)).text, long);
    expect(
      heard(
        ConversationChoices([for (var i = 0; i < maxChoices; i++) long]),
      ).options,
      hasLength(maxChoices),
    );
  });

  test('cannot be built empty, over the limit or with bad text', () {
    expect(() => ConversationChoices([]), throwsArgumentError);
    expect(
      () => ConversationChoices(List.filled(maxChoices + 1, 'x')),
      throwsArgumentError,
    );
    expect(() => ConversationChoices(['']), throwsArgumentError);
    expect(() => ConversationLine(speaker: '', text: 'x'), throwsArgumentError);
    expect(
      () => ConversationLine(speaker: 'x', text: 'a\nb'),
      throwsArgumentError,
    );
  });

  test('the server refuses bytes that do not match their lengths', () {
    expect(whyServer([12, 3, 65]), BadMessage.malformed);
    expect(whyServer([13, 2, 1, 65]), BadMessage.malformed);
    expect(whyServer([13, 1, 1, 65, 0]), BadMessage.malformed);
    expect(whyServer([13, 0]), BadMessage.malformed);
  });

  test('continue and choose round-trip, and refuse extra bytes', () {
    Understood<ClientMessage> read(Uint8List b) =>
        decodeClientMessage(b) as Understood<ClientMessage>;

    expect(
      read(encodeClientMessage(const Continue())).message,
      isA<Continue>(),
    );
    expect(
      (read(encodeClientMessage(Choose(255))).message as Choose).index,
      255,
    );
    expect(() => Choose(256), throwsArgumentError);
    expect(whyClient([11, 0]), BadMessage.tooLarge);
    expect(whyClient([12]), BadMessage.malformed);
  });
}
