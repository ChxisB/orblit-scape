using Scape.Core;

namespace Scape.Client;

/// <summary>One player as the client draws it. <paramref name="Mine"/> is the player's own.</summary>
public readonly record struct ActorSprite(long NetworkId, double X, double Y, int Plane, bool Mine);

/// <summary>An item lying on the ground, with the position it is drawn at now.</summary>
public readonly record struct GroundSprite(long NetworkId, int Item, int Count, double X, double Y, int Plane);

/// <summary>An object standing in view, with the position it is drawn at now.</summary>
public readonly record struct ObjectSprite(long NetworkId, int Def, int State, double X, double Y, int Plane);

/// <summary>A creature standing in view, with the position it is drawn at now.</summary>
public readonly record struct NpcSprite(long NetworkId, int Def, double X, double Y, int Plane);

/// <summary>A line of public chat to draw over the actor <paramref name="NetworkId"/> names.</summary>
public readonly record struct Bubble(long NetworkId, string Text);

/// <summary>A swing that landed on the actor <paramref name="Target"/> names. A <paramref name="Damage"/> of zero is a miss.</summary>
public readonly record struct Splat(long Target, int Damage);

/// <summary>A shot or spell in the air from the actor <paramref name="From"/> names to the one <paramref name="To"/> names. It lands after <paramref name="Ticks"/> ticks.</summary>
public readonly record struct Shot(long From, long To, int Ticks, CombatStyle Style);

/// <summary>The hit points an actor has and the most it can have.</summary>
public readonly record struct HitPoints(int Now, int Max);

/// <summary>A private message from the player called <paramref name="From"/>.</summary>
public readonly record struct Heard(string From, string Text);

/// <summary>A line of public chat, as the listener heard it.</summary>
public readonly record struct Spoken(long Speaker, string Text);

/// <summary>A quest the player has begun, with the text for the stage they are in.</summary>
public readonly record struct JournalEntry(string Title, string Text, bool Done);

/// <summary>
/// The tabs of the chat box. Game is what the server tells the player, Public is what players near
/// them said, and Private is whispers.
/// </summary>
public enum ChatTab
{
    Game,
    Public,
    Private,
}
