import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

import 'trade_messages_test.dart' show roundTrip, whyClient;

void main() {
  test('a move keeps both containers and both slots', () {
    final back = roundTrip(
      MoveItem(
        from: ContainerId.inventory,
        fromSlot: 27,
        to: ContainerId.equipment,
        toSlot: 3,
      ),
    );
    expect(
      (back.from, back.fromSlot, back.to, back.toSlot),
      (ContainerId.inventory, 27, ContainerId.equipment, 3),
    );
  });

  test('the bank is not moved with it', () {
    expect(
      () => MoveItem(
        from: ContainerId.bank,
        fromSlot: 0,
        to: ContainerId.inventory,
        toSlot: 0,
      ),
      throwsArgumentError,
    );
    expect(whyClient([18, 2, 0, 0, 0, 0, 0]), BadMessage.malformed);
  });

  test('a container that does not exist is malformed, not a crash', () {
    expect(whyClient([18, 9, 0, 0, 0, 0, 0]), BadMessage.malformed);
    expect(whyClient([18, 0, 1, 0, 0]), BadMessage.malformed);
  });
}
