import 'package:scape_client/scape_client.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'support/in_process_game.dart';

const double tickSeconds = 0.6;

void main() {
  late InProcessGame game;

  setUp(() => game = InProcessGame());
  tearDown(() => game.dispose());

  Future<GameSession> join(String name) async {
    final session = game.connect()..login(name);
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
    return session;
  }

  Future<void> settle() async {
    await pump();
    game.seconds += tickSeconds;
    await game.tick();
  }

  group('chat', () {
    test('shows over the speaker for those nearby', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');

      ana.say('hello');
      await settle();

      expect(bo.bubbles, [(networkId: ana.avatar!, text: 'hello')]);
      expect(ana.bubbles.single.networkId, ana.avatar);
    });

    test(
      'goes after five seconds, and a newer line replaces an older',
      () async {
        final ana = await join('Ana');
        final bo = await join('Bo');
        ana.say('first');
        await settle();

        game.seconds += bubbleSeconds - 1;
        expect(bo.bubbles.single.text, 'first');

        ana.say('second');
        await settle();
        expect(bo.bubbles.single.text, 'second');

        game.seconds += bubbleSeconds + 1;
        expect(bo.bubbles, isEmpty);
        bo.advance();
        expect(bo.bubbles, isEmpty);
      },
    );

    test('a muted speaker is told, and nobody sees a bubble', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      game.server.social.mute('ana');

      ana.say('hello');
      await settle();

      expect(ana.notices, ['You are muted.']);
      expect(bo.bubbles, isEmpty);
    });
  });

  group('whispers', () {
    test('arrive with the sender and are kept in order', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');

      ana.whisper('bo', 'one');
      ana.whisper('Bo', 'two');
      await settle();

      expect(bo.whispers, [
        (from: 'ana', text: 'one'),
        (from: 'ana', text: 'two'),
      ]);
      expect(ana.whispers, isEmpty);
    });

    test('to someone offline come back as a notice', () async {
      final ana = await join('Ana');

      ana.whisper('Nobody', 'hi');
      await settle();

      expect(ana.notices, ['That player is not online.']);
    });

    test('keep the newest hundred', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');

      for (var i = 0; i < 110; i++) {
        ana.whisper('bo', 'm$i');
        if (i % 10 == 9) await settle();
      }
      await settle();
      await settle();

      expect(bo.whispers, hasLength(100));
      expect(bo.whispers.last.text, 'm109');
      expect(bo.whispers.first.text, 'm10');
    });
  });

  group('lists', () {
    test('follow what the server sends, with friends online', () async {
      final ana = await join('Ana');
      ana.editList(ListKind.friends, 'Bo', add: true);
      ana.editList(ListKind.ignored, 'Cy', add: true);
      await settle();
      expect(ana.friends, [(name: 'bo', online: false)]);
      expect(ana.ignored, ['cy']);

      await join('Bo');
      await settle();
      expect(ana.friends, [(name: 'bo', online: true)]);

      ana.editList(ListKind.ignored, 'cy', add: false);
      await settle();
      expect(ana.ignored, isEmpty);
    });

    test('an ignored player is not heard', () async {
      final ana = await join('Ana');
      final bo = await join('Bo');
      bo.editList(ListKind.ignored, 'Ana', add: true);
      await settle();

      ana.say('hello');
      await settle();

      expect(bo.bubbles, isEmpty);
    });
  });

  test('refuses text it should not send, before it is sent', () async {
    final ana = await join('Ana');

    expect(() => ana.say(''), throwsArgumentError);
    expect(() => ana.whisper('not valid!', 'x'), throwsArgumentError);
    expect(
      () => ana.editList(ListKind.friends, '', add: true),
      throwsArgumentError,
    );
  });
}
