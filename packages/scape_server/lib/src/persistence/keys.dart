/// The store key of a player's save. Names hold letters, digits, spaces,
/// underscores and hyphens, and a store key may not hold a space, so a space
/// becomes a dot. A name has no dot, so two names never share a key.
String saveKeyOf(String player) => 'p-${player.replaceAll(' ', '.')}';

/// The store key of a player's account. The first two characters tell a save
/// from an account, so a name that looks like the other prefix still cannot
/// share a key.
String accountKeyOf(String player) => 'a-${player.replaceAll(' ', '.')}';
