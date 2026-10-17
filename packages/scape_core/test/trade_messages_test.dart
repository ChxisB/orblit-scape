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
    test('a buy and a sell keep the item and the count', () {
      final buy = roundTrip(Buy(item: 300, count: 70000));
      expect((buy.item, buy.count), (300, 70000));
      final sell = roundTrip(Sell(item: maxItemId, count: maxStack));
      expect((sell.item, sell.count), (maxItemId, maxStack));
    });

    test('a buy and a sell have their own tags', () {
      expect(
        encodeClientMessage(Buy(item: 1, count: 1))[0],
        isNot(encodeClientMessage(Sell(item: 1, count: 1))[0]),
      );
    });

    test('a deposit keeps the slot, the count and the tab', () {
      final back = roundTrip(Deposit(slot: 27, count: 5, tab: 7));
      expect((back.slot, back.count, back.tab), (27, 5, 7));
    });

    test('a withdrawal keeps the slot and the count', () {
      final back = roundTrip(Withdraw(slot: 799, count: maxStack));
      expect((back.slot, back.count), (799, maxStack));
    });

    test('cannot be built with a count of nothing or an item that is none', () {
      expect(() => Buy(item: 1, count: 0), throwsArgumentError);
      expect(() => Sell(item: 0, count: 1), throwsArgumentError);
      expect(() => Deposit(slot: 0, count: 0, tab: 0), throwsArgumentError);
      expect(() => Withdraw(slot: 0x10000, count: 1), throwsArgumentError);
    });

    test('bytes with a count of zero are malformed, not a crash', () {
      expect(whyClient([7, 1, 0, 0, 0, 0, 0]), BadMessage.malformed);
      expect(whyClient([9, 0, 0, 0, 0, 0, 0, 0]), BadMessage.malformed);
    });

    test('bytes of the wrong length are malformed', () {
      expect(whyClient([7, 1, 0, 1, 0, 0]), BadMessage.malformed);
      expect(whyClient([10, 1, 0, 1, 0, 0]), BadMessage.malformed);
      expect(whyClient([9, 1, 0, 1, 0, 0, 0]), BadMessage.malformed);
    });

    test('a message over its cap is refused before it is read', () {
      expect(whyClient([8, 1, 0, 1, 0, 0, 0, 0]), BadMessage.tooLarge);
    });
  });

  group('from the server', () {
    test('a shop view keeps the shop and every line', () {
      final back = heard(
        ShopView(
          npc: 4,
          lines: [(item: 2, count: 10), (item: 9, count: maxShopStock)],
        ),
      );
      expect(back.npc, 4);
      expect(back.lines, [
        (item: 2, count: 10),
        (item: 9, count: maxShopStock),
      ]);
    });

    test('a shop view may have no lines and may fill the cap', () {
      expect(heard(ShopView(npc: 1, lines: [])).lines, isEmpty);
      final full = ShopView(
        npc: 1,
        lines: [for (var i = 1; i <= maxShopLines; i++) (item: i, count: i)],
      );
      expect(heard(full).lines, hasLength(maxShopLines));
    });

    test('a shop view with too many lines cannot be built', () {
      expect(
        () => ShopView(
          npc: 1,
          lines: [for (var i = 0; i <= maxShopLines; i++) (item: 1, count: 1)],
        ),
        throwsArgumentError,
      );
    });

    test('a shop view whose length byte lies is malformed', () {
      expect(whyServer([9, 1, 0, 2, 1, 0, 1, 0, 0, 0]), BadMessage.malformed);
    });

    test('bank open and window closed are one byte each', () {
      expect(encodeServerMessage(const BankOpen()), hasLength(1));
      expect(encodeServerMessage(const WindowClosed()), hasLength(1));
      expect(heard(const BankOpen()), isA<BankOpen>());
      expect(heard(const WindowClosed()), isA<WindowClosed>());
      expect(whyServer([10, 0]), BadMessage.tooLarge);
    });
  });
}
