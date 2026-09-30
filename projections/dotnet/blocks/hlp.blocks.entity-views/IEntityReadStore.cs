namespace Harborline.Blocks.EntityViews;

/// <summary>Read and write port for the entity registry that backs the tree and list views.</summary>
public interface IEntityReadStore
{
    /// <summary>Lists entity summaries, filtered to <paramref name="type"/> when it is not null.</summary>
    ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type);
    /// <summary>Returns null only when <paramref name="id"/> is unknown; every operational failure throws <see cref="EntityViewsException"/>.</summary>
    ValueTask<EntityDetail?> GetEntityAsync(string id);
    /// <summary>Returns null only when <paramref name="containerId"/> is unknown.</summary>
    ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf);
    /// <summary>Creates an entity; throws <see cref="EntityViewsException"/> for an unknown type or an invalid entity.</summary>
    ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body);
    /// <summary>Adds an edge between two entities; throws <see cref="EntityViewsException"/> for an unknown endpoint or an invalid edge.</summary>
    ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body);
}

/// <summary>Typed failure of the entity-views stores, carrying a stable <see cref="EntityViewsCodes"/> code.</summary>
public sealed class EntityViewsException(string code, string message) : Exception(message)
{
    /// <summary>The stable machine-readable failure code, one of <see cref="EntityViewsCodes"/>.</summary>
    public string Code { get; } = code;
}

/// <summary>Stable failure codes thrown as <see cref="EntityViewsException"/> by the entity-views stores.</summary>
public static class EntityViewsCodes
{
    /// <summary>The requested entity type is not registered.</summary>
    public const string UnknownType = "views.entity.unknown_type";
    /// <summary>The entity is invalid, for example a blank display name.</summary>
    public const string InvalidEntity = "views.entity.invalid";
    /// <summary>An edge endpoint names an entity that does not exist.</summary>
    public const string UnknownEntity = "views.edge.unknown_entity";
    /// <summary>The edge is invalid, for example it relates an entity to itself.</summary>
    public const string InvalidEdge = "views.edge.invalid";
    /// <summary>The backing store could not be reached.</summary>
    public const string StoreUnavailable = "views.store.unavailable";
    /// <summary>The development preview store was requested in a Production environment.</summary>
    public const string ProductionPreviewForbidden = "views.preview.production_forbidden";
}
