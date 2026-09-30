namespace Harborline.Blocks.EntityViews;

/// <summary>One breadcrumb segment: the entity id and the display label shown for it.</summary>
public sealed record BreadcrumbLabel(string EntityId, string Label);

/// <summary>Turns an entity-id path into labelled breadcrumb segments by reading each entity from the read store.</summary>
public sealed class BreadcrumbResolver(IEntityReadStore store)
{
    /// <summary>Label used for a path segment whose entity cannot be found in the read store.</summary>
    public const string UnresolvedLabel = "Unnamed item";

    /// <summary>Returns one label per path id, in path order; an id that resolves to no entity gets <see cref="UnresolvedLabel"/> rather than an error.</summary>
    public async ValueTask<IReadOnlyList<BreadcrumbLabel>> ResolveAsync(IReadOnlyList<string> path)
    {
        var labels = new List<BreadcrumbLabel>(path.Count);
        foreach (var id in path)
        {
            var detail = await store.GetEntityAsync(id).ConfigureAwait(false);
            labels.Add(new BreadcrumbLabel(id, detail?.DisplayName ?? UnresolvedLabel));
        }

        return labels;
    }
}
