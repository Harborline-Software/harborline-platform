namespace Harborline.Blocks.EntityViews;

public interface IEntityReadStore
{
    ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type);
    /// <summary>Returns null only when <paramref name="id"/> is unknown; every operational failure throws <see cref="EntityViewsException"/>.</summary>
    ValueTask<EntityDetail?> GetEntityAsync(string id);
    /// <summary>Returns null only when <paramref name="containerId"/> is unknown.</summary>
    ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf);
    ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body);
    ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body);
}

public sealed class EntityViewsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class EntityViewsCodes
{
    public const string UnknownType = "views.entity.unknown_type";
    public const string InvalidEntity = "views.entity.invalid";
    public const string UnknownEntity = "views.edge.unknown_entity";
    public const string InvalidEdge = "views.edge.invalid";
    public const string StoreUnavailable = "views.store.unavailable";
    public const string ProductionPreviewForbidden = "views.preview.production_forbidden";
}
