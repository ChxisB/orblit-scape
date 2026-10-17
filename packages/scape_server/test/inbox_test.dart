import 'package:scape_core/scape_core.dart';
import 'package:scape_server/src/inbox.dart';
import 'package:test/test.dart';

Walk walkTo(int x) => Walk(TileCoord(x, 0, 0));

void main() {
  test('hands intents back in the order they came, then is empty', () {
    final inbox = Inbox()
      ..add('ana', walkTo(1))
      ..add('bo', walkTo(2))
      ..add('ana', walkTo(3));

    final xs = [
      for (final intent in inbox.drain()) (intent.player, intent.intent),
    ];

    expect([for (final (p, _) in xs) p], ['ana', 'bo', 'ana']);
    expect([for (final (_, i) in xs) (i as Walk).target.x], [1, 2, 3]);
    expect(inbox.drain(), isEmpty);
  });

  test('keeps the first few of a player and counts the rest as dropped', () {
    final inbox = Inbox(perPlayer: 2);
    for (var i = 0; i < 5; i++) {
      inbox.add('ana', walkTo(i));
    }
    inbox.add('bo', walkTo(9));

    expect(inbox.pending('ana'), 2);
    expect(inbox.pending('bo'), 1);
    expect(inbox.dropped, 3);
  });

  test('draining makes room again', () {
    final inbox = Inbox(perPlayer: 1)..add('ana', walkTo(1));
    inbox.drain();
    inbox.add('ana', walkTo(2));
    expect(inbox.pending('ana'), 1);
    expect(inbox.dropped, 0);
  });

  test('forgets a player who left', () {
    final inbox = Inbox()
      ..add('ana', walkTo(1))
      ..add('bo', walkTo(2))
      ..forget('ana');
    expect(inbox.pending('ana'), 0);
    expect([for (final i in inbox.drain()) i.player], ['bo']);
  });

  test('refuses a limit below one', () {
    expect(() => Inbox(perPlayer: 0), throwsArgumentError);
  });
}
