import 'package:orblit_progress/orblit_progress.dart';
import 'package:orblit_quest/orblit_quest.dart';
import 'package:scape_content/scape_content.dart';
import 'package:test/test.dart';

void main() {
  test('every creature that talks has a dialogue in the story', () {
    final keys = {for (final d in defaultStory.dialogues) d.key};
    final named = [
      for (final npc in defaultNpcs.npcs)
        if (npc.dialogue != null) npc.dialogue!,
    ];
    expect(named, hasLength(3));
    expect(keys, containsAll(named));
  });

  test('every reward names a skill and items the game has', () {
    for (final reward in defaultStory.rewards.values) {
      for (final skill in reward.xp.keys) {
        expect(defaultSkills.find(skill), isNotNull, reason: skill);
      }
      for (final stack in reward.items) {
        expect(defaultItems.find(stack.item), isNotNull);
      }
    }
  });

  test('Goblin Count stays shut until The Dark Lantern is done', () {
    final book = defaultStory.book;
    final fresh = ProgressTable(defaultStory.schema);

    final early = book.advance(fresh, 'tally', 1);
    final lit = book.advance(fresh, 'lantern', 3);
    final late = book.advance((lit as Moved).table, 'tally', 1);

    expect(early, isA<Refused>());
    expect(late, isA<Moved>());
  });
}
