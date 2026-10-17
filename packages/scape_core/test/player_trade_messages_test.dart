import 'dart:typed_data';

import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

Understood<T> read<T>(Decoded<T> decoded) => decoded as Understood<T>;

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

TradeView full() => TradeView(
  partner: 'bo',
  mine: [ItemStack(5, 3), ItemStack(9, maxStack)],
  theirs: [for (var i = 1; i <= maxTradeLines; i++) ItemStack(i, i)],
  confirming: true,
  youAccepted: true,
  theyAccepted: false,
);

void main() {
  test('an offer, an accept and a decline round-trip', () {
    final offer =
        read(
              decodeClientMessage(
                encodeClientMessage(TradeOffer(item: 7, count: 0)),
              ),
            ).message
            as TradeOffer;

    expect((offer.item, offer.count), (7, 0));
    expect(
      read(decodeClientMessage(encodeClientMessage(const TradeAccept()))),
      isA<Understood<ClientMessage>>(),
    );
    expect(
      read(
        decodeClientMessage(encodeClientMessage(const TradeDecline())),
      ).message,
      isA<TradeDecline>(),
    );
  });

  test('an offer names a real item and a count the stack can hold', () {
    expect(() => TradeOffer(item: 0, count: 1), throwsArgumentError);
    expect(() => TradeOffer(item: 1, count: -1), throwsArgumentError);
    expect(() => TradeOffer(item: 1, count: maxStack + 1), throwsArgumentError);
    expect(whyClient([13, 0, 0, 1, 0, 0, 0]), BadMessage.malformed);
    expect(whyClient([14, 0]), BadMessage.tooLarge);
  });

  test('a view keeps both sides, the stage and who has accepted', () {
    final back =
        read(decodeServerMessage(encodeServerMessage(full()))).message
            as TradeView;

    expect(back.partner, 'bo');
    expect(back.mine, [ItemStack(5, 3), ItemStack(9, maxStack)]);
    expect(back.theirs, hasLength(maxTradeLines));
    expect(back.theirs.last, ItemStack(maxTradeLines, maxTradeLines));
    expect(
      (back.confirming, back.youAccepted, back.theyAccepted),
      (true, true, false),
    );
  });

  test('a view with two empty sides is just a name and flags', () {
    final view = TradeView(
      partner: 'bo',
      mine: [],
      theirs: [],
      confirming: false,
      youAccepted: false,
      theyAccepted: true,
    );

    final back =
        read(decodeServerMessage(encodeServerMessage(view))).message
            as TradeView;

    expect(back.mine, isEmpty);
    expect(back.theyAccepted, isTrue);
  });

  test('a view cannot be built with a bad name or too many lines', () {
    TradeView make({String partner = 'bo', int lines = 1}) => TradeView(
      partner: partner,
      mine: [for (var i = 0; i < lines; i++) ItemStack(1, 1)],
      theirs: [],
      confirming: false,
      youAccepted: false,
      theyAccepted: false,
    );

    expect(() => make(partner: ''), throwsArgumentError);
    expect(() => make(lines: maxTradeLines + 1), throwsArgumentError);
  });

  test('the server refuses a view that does not match its lengths', () {
    final good = encodeServerMessage(full()).toList();

    expect(whyServer([...good, 0]), BadMessage.malformed);
    expect(
      whyServer([14, ...List.filled(5 + maxNameChars + 12 * maxTradeLines, 0)]),
      BadMessage.tooLarge,
    );
    expect(whyServer(good.sublist(0, good.length - 1)), BadMessage.malformed);
    expect(whyServer([14, 9, 98]), BadMessage.malformed);
    expect(whyServer([14, 2, 98, 111, 0, 0]), BadMessage.malformed);
    expect(
      whyServer([14, 2, 98, 111, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0]),
      BadMessage.malformed,
    );
  });

  test('a player can be the target of an interaction', () {
    expect(InteractKind.player, 5);
    expect(Interact(kind: InteractKind.player, id: 9, option: 0).id, 9);
  });
}
