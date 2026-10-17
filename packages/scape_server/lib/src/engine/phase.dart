/// The parts of a tick, in the order they run.
///
/// [outbound] runs the game's own outbound systems, then the host publishes.
/// The host times its own stages (capture, interest, send) under their own
/// names, so they follow [outbound] in a summary rather than sit inside it.
enum Phase { inbound, actions, npcs, movement, combat, outbound }
