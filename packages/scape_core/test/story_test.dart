import 'package:orblit_dialogue/orblit_dialogue.dart';
import 'package:orblit_quest/orblit_quest.dart';
import 'package:scape_core/scape_core.dart';
import 'package:test/test.dart';

Quest quest(String key, [int stages = 3]) => Quest(
  key: key,
  title: key,
  stages: [for (var i = 0; i < stages; i++) Stage(id: 's$i', journal: '$i')],
);

Story story({
  Map<String, QuestStep> steps = const {},
  Map<String, QuestReward> rewards = const {},
}) => Story(
  quests: [quest('lamp'), quest('crabs')],
  dialogues: <Dialogue>[],
  steps: steps,
  rewards: rewards,
);

void main() {
  test('gives each quest a whole, in the order the quests were given', () {
    final schema = story().schema;

    expect(schema.slotCount, 2);
    expect(schema.placeOf(questStageName('lamp')).slot, 0);
    expect(schema.placeOf(questStageName('crabs')).slot, 1);
  });

  test('a step must name a quest and a stage it reaches', () {
    expect(
      () => story(steps: {'go': (quest: 'gone', stage: 1)}),
      throwsArgumentError,
    );
    expect(
      () => story(steps: {'go': (quest: 'lamp', stage: 3)}),
      throwsRangeError,
    );
    expect(
      () => story(steps: {'go': (quest: 'lamp', stage: 0)}),
      throwsRangeError,
    );
    expect(story(steps: {'go': (quest: 'lamp', stage: 2)}).steps, hasLength(1));
  });

  test('a reward must belong to a quest and give something', () {
    expect(
      () => story(
        rewards: {
          'gone': QuestReward(xp: {'a': 1}),
        },
      ),
      throwsArgumentError,
    );
    expect(() => QuestReward(), throwsArgumentError);
    expect(() => QuestReward(xp: {'a': 0}), throwsArgumentError);
    expect(
      () =>
          QuestReward(items: List.filled(maxRewardItems + 1, ItemStack(1, 1))),
      throwsArgumentError,
    );
  });

  test('keeps what it was built from even if that changes afterwards', () {
    final steps = {'go': (quest: 'lamp', stage: 1)};
    final built = story(steps: steps);
    steps.clear();

    expect(built.steps, hasLength(1));
    expect(() => built.steps.clear(), throwsUnsupportedError);
  });
}
