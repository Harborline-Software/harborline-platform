namespace Harborline.Blocks.Aggregates;

/// <summary>Authenticated execution context supplied by the composing host.</summary><param name="TenantId">Trusted tenant identifier.</param><param name="ActorId">Trusted actor identifier.</param>
public sealed record AggregateExecutionContext(string TenantId, string ActorId);
/// <summary>Effective host limits that definition bounds may not exceed.</summary><param name="MaxInputRows">Host input limit.</param><param name="MaxGroups">Host group limit.</param><param name="MaxResultCells">Host cell limit.</param>
public sealed record AggregateHostBounds(int MaxInputRows, int MaxGroups, int MaxResultCells);
/// <summary>One flat source row.</summary><param name="Fields">Exact source values by field.</param>
public sealed record AggregateRow(IReadOnlyDictionary<string, AggregateValue> Fields);
/// <summary>A consistent row snapshot.</summary><param name="Token">Opaque snapshot token.</param><param name="Rows">Single-use or replayable asynchronous row stream.</param>
public sealed record AggregateRowSnapshot(string Token, IAsyncEnumerable<AggregateRow> Rows);

/// <summary>Opens one authorized, consistent, bounded logical row source.</summary>
public interface IAggregateRowSource
{
    /// <summary>Opens a snapshot without provider query vocabulary.</summary><param name="context">Trusted context.</param><param name="sourceRef">Logical source reference.</param><param name="requiredFields">Required source fields.</param><param name="bounds">Effective bounds.</param><param name="cancellationToken">Cancellation token.</param><returns>One snapshot.</returns>
    ValueTask<AggregateRowSnapshot> OpenSnapshotAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, AggregateBounds bounds, CancellationToken cancellationToken);
}

/// <summary>Authorizes source fields before row enumeration.</summary>
public interface IAggregateAuthorization
{
    /// <summary>Checks whether the actor may evaluate all required fields.</summary><param name="context">Trusted context.</param><param name="sourceRef">Logical source.</param><param name="requiredFields">Fields used by the plan.</param><param name="cancellationToken">Cancellation token.</param><returns><see langword="true"/> when authorized.</returns>
    ValueTask<bool> AuthorizeAsync(AggregateExecutionContext context, string sourceRef, IReadOnlySet<string> requiredFields, CancellationToken cancellationToken);
}

/// <summary>Persistence seam for immutable, tenant-scoped definition revisions.</summary>
public interface IAggregateDefinitionStore
{
    /// <summary>Gets a revision without revealing cross-tenant existence.</summary><param name="tenantId">Trusted tenant.</param><param name="definitionId">Definition identifier.</param><param name="revision">Optional exact revision.</param><param name="cancellationToken">Cancellation token.</param><returns>The visible revision or null.</returns>
    ValueTask<AggregateDefinition?> GetAsync(string tenantId, string definitionId, long? revision, CancellationToken cancellationToken);
    /// <summary>Appends an immutable revision under optimistic concurrency.</summary><param name="tenantId">Trusted tenant.</param><param name="definition">New revision.</param><param name="expectedRevision">Expected prior revision.</param><param name="cancellationToken">Cancellation token.</param><returns>The stored revision.</returns>
    ValueTask<AggregateDefinition> AppendAsync(string tenantId, AggregateDefinition definition, long expectedRevision, CancellationToken cancellationToken);
}
