namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>
/// drilldown-model.md:18 — a facet is the same object seen differently; it is a place, so it is
/// addressable by id and carries the count from the same query (drilldown-model.md:44).
/// </summary>
public sealed record DetailPanelFacet(string Id, string Label, int Count);

/// <summary>drilldown-model.md:22 — following one crosses to a <em>different</em> object.</summary>
public sealed record DetailPanelRelation(string Id, string Label, string TargetId);

/// <summary>
/// One inspected object. <paramref name="Route"/> is the route <c>Open as page</c> promotes to
/// (drilldown-model.md:23); the host owns navigation, the panel only declares the action.
/// </summary>
public sealed record DetailPanelSubject(
    string Id,
    string Title,
    string Route,
    IReadOnlyList<DetailPanelFacet> Facets,
    IReadOnlyList<DetailPanelRelation>? Relations = null);
