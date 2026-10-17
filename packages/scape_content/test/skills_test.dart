import 'package:scape_content/scape_content.dart';
import 'package:test/test.dart';

void main() {
  test('the first game has nine skills, each capped at level 50', () {
    expect(
      [for (final s in defaultSkills.skills) s.id],
      [
        'woodcutting',
        'mining',
        'fishing',
        'cooking',
        'smithing',
        'firemaking',
        'fletching',
        'fighting',
        'health',
      ],
    );
    expect(defaultSkills.skills.every((s) => s.levelCap == 50), isTrue);
  });

  test('level 50 costs the figure the doc comment gives', () {
    expect(defaultSkills.curve.xpForLevel(50), 542626);
  });
}
