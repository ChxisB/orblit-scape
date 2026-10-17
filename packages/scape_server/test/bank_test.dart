import 'package:orblit_net/orblit_net.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:scape_server/scape_server.dart';
import 'package:test/test.dart';

import 'support/fake_time.dart';
import 'support/no_pacer.dart';
import 'support/open_world.dart';
import 'support/test_client.dart';

TileCoord at(int x, int y) => TileCoord(x, y, 0);

extension on TestClient {
  List<String> get notices => [
    for (final Notice(:text) in messages.whereType<Notice>()) text,
  ];

  Future<void> useBooth(int networkId) =>
      send(Interact(kind: InteractKind.object, id: networkId, option: 0));

  Future<void> deposit(int slot, int count, {int tab = 0}) =>
      send(Deposit(slot: slot, count: count, tab: tab));

  Future<void> withdraw(int slot, int count) =>
      send(Withdraw(slot: slot, count: count));
}

void main() {
  late ScapeServer server;
  late PlacedObject booth;
  final clients = <TestClient>[];

  Future<void> tick([int times = 1]) async {
    for (var i = 0; i < times; i++) {
      server.loop.runTick();
      await pump();
    }
  }

  Container inventory() =>
      server.items.container('ana', ContainerId.inventory)!;

  Container bank() => server.items.container('ana', ContainerId.bank)!;

  /// Joins Ana and, when [open] is set, has her open the booth.
  Future<TestClient> ana({bool open = true}) async {
    final client = TestClient(server);
    clients.add(client);
    await client.login('Ana');
    await tick(2);
    if (open) {
      await client.useBooth(booth.networkId);
      await tick(4);
    }
    return client;
  }

  setUp(() {
    server = ScapeServer(
      clock: TickClock(period: tickPeriod, now: FakeTime().read),
      pacer: NoPacer(),
      log: (_) {},
      map: openWorld(),
      spawn: spawnTile,
      skills: defaultSkills,
      items: defaultItems,
      objects: defaultObjects,
      nodes: defaultNodes,
      npcs: defaultNpcs,
      combat: defaultCombat,
      shops: defaultShops,
    );
    booth = server.objects.place(ObjectId.bankBooth, at(33, 32));
  });

  tearDown(() async {
    for (final client in clients) {
      await client.dispose();
    }
    clients.clear();
    await server.dispose();
  });

  test('using the booth opens the bank', () async {
    final player = await ana();

    expect(player.messages.whereType<BankOpen>(), hasLength(1));
  });

  test('a deposit moves part of a stack into the chosen tab', () async {
    final player = await ana();
    inventory().add(ItemId.feathers, 10);

    await player.deposit(0, 4, tab: 3);
    await tick(2);

    expect(inventory().countOf(ItemId.feathers), 6);
    expect(bank().countOf(ItemId.feathers), 4);
    expect(bank().occupied(tab: 3), hasLength(1));
    expect(bank().occupied(tab: 0), isEmpty);
  });

  test('a withdrawal moves a stack back', () async {
    final player = await ana();
    bank().add(ItemId.feathers, 3, tab: 0);

    await player.withdraw(0, 2);
    await tick(2);

    expect(inventory().countOf(ItemId.feathers), 2);
    expect(bank().countOf(ItemId.feathers), 1);
  });

  test('a withdrawal with no room changes nothing', () async {
    final player = await ana();
    bank().add(ItemId.copperOre, 3, tab: 0);
    while (inventory().occupied().length < inventorySize) {
      inventory().add(ItemId.bones, 1);
    }

    await player.withdraw(0, 3);
    await tick(2);

    expect(player.notices, contains('There is no room for that.'));
    expect(bank().countOf(ItemId.copperOre), 3);
  });

  test('an empty slot is refused', () async {
    final player = await ana();

    await player.withdraw(5, 1);
    await tick(2);

    expect(player.notices, contains('There is nothing in that slot.'));
  });

  test('a tab that does not exist is refused', () async {
    final player = await ana();
    inventory().add(ItemId.feathers, 1);

    await player.deposit(0, 1, tab: bankTabs);
    await tick(2);

    expect(inventory().countOf(ItemId.feathers), 1);
    expect(bank().countOf(ItemId.feathers), 0);
  });

  test('nothing moves while the bank is closed', () async {
    final player = await ana(open: false);
    inventory().add(ItemId.feathers, 5);
    bank().add(ItemId.copperOre, 1, tab: 0);

    await player.deposit(0, 5);
    await player.withdraw(0, 1);
    await tick(2);

    expect(inventory().countOf(ItemId.feathers), 5);
    expect(bank().countOf(ItemId.copperOre), 1);
  });

  test('walking away closes it and ends the use of it', () async {
    final player = await ana();
    inventory().add(ItemId.feathers, 5);

    await player.send(Walk(at(60, 60)));
    await tick(12);
    await player.deposit(0, 5);
    await tick(2);

    expect(player.messages.whereType<WindowClosed>(), hasLength(1));
    expect(inventory().countOf(ItemId.feathers), 5);
  });

  test('the bank is searchable by name', () async {
    await ana();
    bank()
      ..add(ItemId.copperOre, 1, tab: 0)
      ..add(ItemId.logs, 1, tab: 2);

    expect(bank().search('ore'), hasLength(1));
    expect(bank().search('LOGS', tab: 2), hasLength(1));
    expect(bank().search('logs', tab: 0), isEmpty);
  });
}
