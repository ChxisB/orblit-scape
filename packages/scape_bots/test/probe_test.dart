import 'package:scape_bots/scape_bots.dart';
import 'package:scape_content/scape_content.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

TileCoord _at(int x, int y) => TileCoord(x, y, 0);

void main() {
  late BotRun run;
  late ServerProbe probe;

  // The bots never act here, so they stand where they spawned.
  setUp(() async {
    run = await BotRun.start(bots: 3);
    probe = run.probe;
    await probe.step(3);
  });

  tearDown(() => run.dispose());

  test('lists the players in the world', () {
    expect(probe.players(), ['bot-0000', 'bot-0001', 'bot-0002']);
  });

  test('reads a player, and nothing for a name that is not in the world', () {
    final view = probe.player('bot-0001')!;

    expect(view.tile, _at(32, 32));
    expect(view.moving, isFalse);
    expect(view.fighting, isFalse);
    expect(view.health?.now, view.health?.max);
    expect(probe.player('nobody'), isNull);
  });

  test('observes the players, creatures and objects in an area', () {
    final area = probe.area(_at(32, 32), 6);

    expect(area.players, hasLength(3));
    expect(area.creatures.map((c) => c.def), everyElement(NpcId.chicken));
    expect(area.creatures, hasLength(4));
    expect(area.objects.map((o) => o.def), everyElement(ObjectId.tree));
    expect(area.objects, hasLength(4));
  });

  test('leaves out what is outside the area', () {
    expect(probe.area(_at(32, 32), 1).objects, isEmpty);
    expect(probe.area(_at(32, 32), 2).objects, hasLength(4));
    final upstairs = probe.area(TileCoord(32, 32, 1), 6);

    expect(upstairs.players, isEmpty);
    expect(upstairs.creatures, isEmpty);
    expect(upstairs.objects, isEmpty);
    final far = probe.area(_at(100, 100), 5);

    expect(far.players, isEmpty);
    expect(far.creatures, isEmpty);
    expect(far.objects, isEmpty);
  });

  test('includes a player exactly at the radius on each axis', () {
    expect(probe.area(_at(35, 32), 3).players, hasLength(3));
    expect(probe.area(_at(35, 32), 2).players, isEmpty);
    expect(probe.area(_at(32, 35), 3).players, hasLength(3));
    expect(probe.area(_at(32, 35), 2).players, isEmpty);
  });

  test('observes an area at the edge of the world', () {
    expect(probe.area(_at(0, 0), 3).players, isEmpty);
  });

  test('refuses an area that is too wide or has a negative radius', () {
    expect(() => probe.area(_at(32, 32), maxAreaRadius + 1), throwsRangeError);
    expect(() => probe.area(_at(32, 32), -1), throwsRangeError);
  });

  test('delivers what a client sent before it runs the tick', () async {
    run.bots.first.session.walkTo(TileCoord(36, 32, 0));
    await probe.step(1);

    expect(probe.player('bot-0000')!.moving, isTrue);
  });

  test('samples the length of each tick it steps', () async {
    final before = probe.tickTimes.length;
    await probe.step(5);

    expect(probe.tickTimes, hasLength(before + 5));
    expect(probe.tickTimes.every((time) => time >= Duration.zero), isTrue);
    expect(() => probe.step(-1), throwsRangeError);
  });
}
