namespace Harborline.Kernel.WorkItems;

/// <summary>Lifecycle of one durable process-backed work item.</summary>
public enum WorkItemStatus
{
    /// <summary>An automated continuation may run.</summary>
    Running = 0,
    /// <summary>The process is parked awaiting a typed external or human result.</summary>
    Parked = 1,
    /// <summary>The process completed successfully.</summary>
    Completed = 2,
    /// <summary>The process terminated unsuccessfully.</summary>
    Failed = 3,
}

/// <summary>Immutable caller projection of current work-item state.</summary>
public sealed record WorkItemSnapshot(
    string Id,
    string SubjectRef,
    string DefinitionKey,
    string DefinitionVersion,
    string CurrentStep,
    int Iteration,
    long Version,
    WorkItemStatus Status,
    string StateJson,
    string BasisJson,
    IReadOnlyList<WorkItemOutcome> AllowedOutcomes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A typed lifecycle edge admitted when the item is created.</summary>
public sealed record WorkItemOutcome(
    string Id,
    string FromStep,
    string NextStep,
    WorkItemStatus NextStatus,
    bool IsLoopBack = false);

/// <summary>Creates one process-backed work item in the current tenant.</summary>
public sealed record CreateWorkItemRequest
{
    /// <summary>Stable caller-selected identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Opaque domain subject, such as an inspection submission reference.</summary>
    public required string SubjectRef { get; init; }
    /// <summary>Workflow or process definition key.</summary>
    public required string DefinitionKey { get; init; }
    /// <summary>Definition version pinned for replay.</summary>
    public required string DefinitionVersion { get; init; }
    /// <summary>Initial position.</summary>
    public required string InitialStep { get; init; }
    /// <summary>Initial lifecycle.</summary>
    public WorkItemStatus InitialStatus { get; init; } = WorkItemStatus.Parked;
    /// <summary>Opaque JSON process state.</summary>
    public string StateJson { get; init; } = "{}";
    /// <summary>Opaque JSON decision basis shown before action.</summary>
    public string BasisJson { get; init; } = "{}";
    /// <summary>Closed typed transitions; the kernel, not the caller, derives the next lifecycle.</summary>
    public required IReadOnlyList<WorkItemOutcome> AllowedOutcomes { get; init; }
    /// <summary>Caller-stable retry key.</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>Moves one work item from its expected current version and step.</summary>
public sealed record TransitionWorkItemRequest
{
    /// <summary>Target work item.</summary>
    public required string Id { get; init; }
    /// <summary>Optimistic concurrency token read from the snapshot.</summary>
    public required long ExpectedVersion { get; init; }
    /// <summary>Caller-stable retry key.</summary>
    public required string IdempotencyKey { get; init; }
    /// <summary>One allowed outcome ID for the current step.</summary>
    public required string OutcomeId { get; init; }
    /// <summary>Opaque typed result recorded for replay.</summary>
    public string ResultJson { get; init; } = "{}";
}

/// <summary>One immutable transactional-outbox message.</summary>
public sealed record WorkItemOutboxMessage(string MessageId, string Kind, string PayloadJson);

/// <summary>Stable mutation disposition.</summary>
public enum WorkItemMutationDisposition
{
    /// <summary>A new mutation committed.</summary>
    Committed,
    /// <summary>The exact request was already committed and its receipt was replayed.</summary>
    Replayed,
    /// <summary>The retry key was previously used for different content.</summary>
    IdempotencyConflict,
    /// <summary>The expected version or step is stale.</summary>
    VersionConflict,
    /// <summary>No current-tenant item exists.</summary>
    NotFound,
    /// <summary>The requested lifecycle move is invalid.</summary>
    InvalidTransition,
    /// <summary>No active authenticated tenant scope is available.</summary>
    Denied,
}

/// <summary>Mutation result and original replayable receipt.</summary>
public sealed record WorkItemMutationResult(
    WorkItemMutationDisposition Disposition,
    WorkItemSnapshot? Snapshot,
    string? ResultJson)
{
    /// <summary>True when the requested mutation is committed, including exact replay.</summary>
    public bool IsSuccess => Disposition is WorkItemMutationDisposition.Committed or WorkItemMutationDisposition.Replayed;
}

/// <summary>Tenant-scoped deep module for durable work-item behavior.</summary>
public interface IWorkItemKernel
{
    /// <summary>Creates one item, or replays the original receipt.</summary>
    Task<WorkItemMutationResult> CreateAsync(CreateWorkItemRequest request, CancellationToken cancellationToken = default);
    /// <summary>Gets one current-tenant item; foreign and absent IDs both return null.</summary>
    Task<WorkItemSnapshot?> GetAsync(string workItemId, CancellationToken cancellationToken = default);
    /// <summary>Lists current-tenant running and parked items, newest update first.</summary>
    Task<IReadOnlyList<WorkItemSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default);
    /// <summary>Atomically transitions one item, or returns a typed non-mutating outcome.</summary>
    Task<WorkItemMutationResult> TransitionAsync(TransitionWorkItemRequest request, CancellationToken cancellationToken = default);
}
