/// The store key of a player's save. Names hold letters, digits, spaces,
/// underscores and hyphens, and a store key may not hold a space, so a space
/// becomes a dot. A name has no dot, so two names never share a key.
String saveKeyOf(String player) => 'p-${player.replaceAll(' ', '.')}';

/// The store key of a player's account. The first two characters tell a save
/// from an account, so a name that looks like the other prefix still cannot
/// share a key.
String accountKeyOf(String player) => 'a-${player.replaceAll(' ', '.')}';

/// The store key of the journal for a trade between [a] and [b]. The names
/// are sorted, so both players give one key. The first name's length keeps
/// two pairs from sharing a key, and the `t-` prefix keeps it from a save.
String tradeKeyOf(String a, String b) {
  final names = [a, b].map((n) => n.replaceAll(' ', '.')).toList()..sort();
  return 't-${names[0].length}-${names[0]}-${names[1]}';
}
