namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The definition families whose archive namespaces are isolated from each other, as pinned.</summary>
public enum DefinitionKind
{
    /// <summary>Form definitions.</summary>
    Forms,
    /// <summary>Workflow definitions.</summary>
    Workflows,
}

/// <summary>Identifies one definition inside a tenant- and kind-scoped archive namespace.</summary>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="Kind">The definition kind namespace.</param>
/// <param name="DefinitionKey">The definition's key inside that namespace.</param>
public sealed record DefinitionLifecycleKey(string Tenant, DefinitionKind Kind, string DefinitionKey);

/// <summary>
/// The durable server-side archive lifecycle (migration ticket 090 ruling, 2026-08-17,
/// superseding the pinned device-local localStorage store). Archive changes visibility
/// only; it never deletes an authored definition.
/// </summary>
public interface IDefinitionLifecycleStore
{
    /// <summary>Marks the definition archived. Idempotent, as pinned.</summary>
    /// <param name="key">The definition to archive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask ArchiveAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes the archived mark. Idempotent, as pinned.</summary>
    /// <param name="key">The definition to unarchive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask UnarchiveAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default);

    /// <summary>Reports whether the definition is currently archived.</summary>
    /// <param name="key">The definition to inspect.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> IsArchivedAsync(DefinitionLifecycleKey key, CancellationToken cancellationToken = default);

    /// <summary>Streams the archived definition keys inside one tenant- and kind-scoped namespace.</summary>
    /// <param name="tenant">The owning tenant.</param>
    /// <param name="kind">The definition kind namespace.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    IAsyncEnumerable<string> ListArchivedAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default);
}
