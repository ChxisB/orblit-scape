import 'package:orblit_net/orblit_net.dart';

/// Whether [clientId] may be told about the entity [networkId].
typedef Allows = bool Function(String clientId, int networkId);

/// An interest rule that also asks [_allows], so a feature can keep an entity
/// from some clients. [_inner] still decides who is near.
final class FilteredInterest implements InterestRule {
  FilteredInterest(this._inner, this._allows);

  final InterestRule _inner;
  final Allows _allows;

  @override
  void observe(WorldSnapshot world, Map<String, List<int>> focus) =>
      _inner.observe(world, focus);

  @override
  bool isRelevant(String clientId, int networkId) =>
      _allows(clientId, networkId) && _inner.isRelevant(clientId, networkId);
}
