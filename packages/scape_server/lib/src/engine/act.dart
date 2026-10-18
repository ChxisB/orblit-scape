import 'package:scape_core/scape_core.dart';

/// Starts [state] on the actor [entity], which shows it once. A feature that
/// swings, gets hurt or works calls it and knows nothing of how it is drawn.
typedef Act = void Function(int entity, ActorState state);
