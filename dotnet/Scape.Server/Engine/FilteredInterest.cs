using Orblit.Net;

namespace Scape.Server;

/// <summary>
/// An interest rule that also asks <c>allows</c>, so a feature can keep an entity from some
/// clients. The inner rule still decides who is near.
/// </summary>
/// <param name="allows">Whether a client may be told about the entity with a network id.</param>
internal sealed class FilteredInterest(IInterestRule inner, Func<string, ulong, bool> allows) : IInterestRule
{
    public void Observe(WorldSnapshot world, IReadOnlyDictionary<string, IReadOnlyList<ulong>> focus) =>
        inner.Observe(world, focus);

    public bool IsRelevant(string clientId, ulong networkId) =>
        allows(clientId, networkId) && inner.IsRelevant(clientId, networkId);
}
