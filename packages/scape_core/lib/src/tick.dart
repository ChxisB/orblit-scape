/// How long a game tick lasts. The server steps the world this often and a
/// client blends between the states it is sent over about as long.
const Duration tickPeriod = Duration(milliseconds: 600);
