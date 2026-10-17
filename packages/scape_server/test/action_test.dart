import 'package:scape_server/src/engine/action.dart';
import 'package:test/test.dart';

void main() {
  late ActionQueue queue;
  late List<String> ran;

  final chat = ActionKind('chat');
  final skill = ActionKind('skill', delay: 3);
  final walk = ActionKind('walk', cancels: const CancelsAll());
  final eat = ActionKind('eat', cancels: CancelsOnly({skill}));
  final guarded = ActionKind('guarded', cancels: CancelsAll(except: {chat}));

  void submit(int actor, ActionKind kind, [String? label]) {
    queue.submit(actor, kind, () => ran.add(label ?? kind.name));
  }

  void tick([int times = 1]) {
    for (var i = 0; i < times; i++) {
      queue
        ..advance()
        ..runDue();
    }
  }

  setUp(() {
    queue = ActionQueue();
    ran = [];
  });

  group('ActionKind', () {
    test('refuses an empty name and a negative delay', () {
      expect(() => ActionKind(''), throwsArgumentError);
      expect(() => ActionKind('x', delay: -1), throwsArgumentError);
    });

    test('a walk cancels everything, a chat cancels nothing', () {
      expect(walk.interrupts(skill), isTrue);
      expect(walk.interrupts(walk), isTrue);
      expect(chat.interrupts(skill), isFalse);
    });

    test('cancels all but the exceptions, or only the listed kinds', () {
      expect(guarded.interrupts(chat), isFalse);
      expect(guarded.interrupts(skill), isTrue);
      expect(eat.interrupts(skill), isTrue);
      expect(eat.interrupts(walk), isFalse);
    });
  });

  group('ActionQueue', () {
    test('runs an action with no delay in the tick it was queued', () {
      queue.advance();
      submit(1, chat);
      queue.runDue();
      expect(ran, ['chat']);
    });

    test('holds an action until its delay has passed', () {
      queue.advance();
      submit(1, skill);
      queue.runDue();
      tick(2);
      expect(ran, isEmpty);
      tick();
      expect(ran, ['skill']);
    });

    test('runs an action once', () {
      queue.advance();
      submit(1, chat);
      queue.runDue();
      tick(3);
      expect(ran, ['chat']);
    });

    test('an action cancelled before its delay ends never runs', () {
      queue.advance();
      submit(1, skill);
      tick();
      submit(1, walk);
      tick(10);
      expect(ran, ['walk']);
    });

    test('an action that cancels nothing leaves a running one alone', () {
      queue.advance();
      submit(1, skill);
      tick();
      submit(1, chat);
      tick(5);
      expect(ran, ['chat', 'skill']);
    });

    test('a cancel only reaches the same actor', () {
      queue.advance();
      submit(1, skill, 'one');
      submit(2, skill, 'two');
      tick();
      submit(1, walk);
      tick(5);
      expect(ran, ['walk', 'two']);
    });

    test('a cancel by a listed kind spares the others', () {
      queue.advance();
      submit(1, skill);
      submit(1, walk, 'later walk');
      tick();
      submit(1, eat);
      tick(5);
      expect(ran, contains('later walk'));
      expect(ran, isNot(contains('skill')));
    });

    test('two actions queued in one tick run in the order they came', () {
      queue.advance();
      submit(1, chat, 'first');
      submit(2, chat, 'second');
      submit(1, chat, 'third');
      queue.runDue();
      expect(ran, ['first', 'second', 'third']);
    });

    test('an earlier action in a pass can cancel a later one', () {
      queue.advance();
      queue.submit(1, chat, () {
        ran.add('chat');
        submit(1, walk);
      });
      queue.submit(1, skill, () => ran.add('skill'));
      queue.runDue();
      tick(5);
      expect(ran, ['chat', 'walk']);
    });

    test('an action queued while others run waits for the next tick', () {
      queue.advance();
      queue.submit(1, chat, () {
        ran.add('outer');
        submit(2, chat, 'inner');
      });
      queue.runDue();
      expect(ran, ['outer']);
      tick();
      expect(ran, ['outer', 'inner']);
    });

    test('counts what is waiting for one actor', () {
      queue.advance();
      submit(1, skill);
      submit(1, skill);
      submit(2, skill);
      expect(queue.pending(1), 2);
      expect(queue.pending(3), 0);
      submit(1, walk);
      expect(queue.pending(1), 1);
    });
  });
}
