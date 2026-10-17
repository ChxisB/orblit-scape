/// A clock the test moves by hand.
final class FakeTime {
  Duration now = Duration.zero;

  Duration read() => now;

  void advance(Duration by) => now += by;
}
