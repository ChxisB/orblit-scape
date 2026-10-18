/// Bots for a scape server, and the tools an agent uses to look at one.
library;

export 'src/bot.dart' show Bot;
export 'src/cli.dart' show botsUsage, runBots;
export 'src/probe.dart'
    show AreaView, PlayerView, Seen, ServerProbe, maxAreaRadius;
export 'src/report.dart'
    show BotReport, Machine, currentMachine, formatMs, percentile;
export 'src/run.dart' show BotRun, openGround;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapeBotsVersion = '0.1.0';
