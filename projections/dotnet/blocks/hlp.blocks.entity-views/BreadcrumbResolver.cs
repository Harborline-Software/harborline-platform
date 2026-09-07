namespace Harborline.Blocks.EntityViews;

public sealed record BreadcrumbLabel(string EntityId, string Label);

public sealed class BreadcrumbResolver(IEntityReadStore store)
{
    public const string UnresolvedLabel = "Unnamed item";

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
