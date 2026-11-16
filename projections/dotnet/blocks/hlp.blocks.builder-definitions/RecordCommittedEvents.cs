using System.Text.Json.Nodes;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// One committed Records write as Records publishes it after commit (ADR-0038 React stage, ADR 0124 decision 2): the record
/// type, record id, version and source <c>operation_id</c>, and the principal whose committed write produced it (ADR 0131
/// decision 2). Every field is read from the committed journal entry, never from a hand-off's context, so a redelivery is
/// equal to the first delivery.
/// </summary>
/// <param name="Position">The entry's 1-based position in the journal it committed into: the feed's resumable cursor.</param>
/// <param name="EventId">The stable event identity: the source operation's idempotency key. A consumer dedupes on it.</param>
/// <param name="EventType"><see cref="RecordCommittedEvents.Created"/> or <see cref="RecordCommittedEvents.Updated"/>.</param>
/// <param name="Tenant">The write's tenant.</param>
/// <param name="RecordTypeId">The bound Record Type.</param>
/// <param name="RecordId">The written record.</param>
/// <param name="Version">The published Record Type version the write was bound to.</param>
/// <param name="Revision">The record's revision after the write: 1 for a create, the temporal revision for an amend.</param>
/// <param name="OperationId">The source <c>operation_id</c> (records-run-3).</param>
/// <param name="Principal">The principal the event acts for: an accepted command's submitter, otherwise the caller the gate
/// decided for (ADR-0154 decision 1).</param>
/// <param name="Executor">An accepted command's worker; null when the principal executed the write itself.</param>
public sealed record RecordCommittedEvent(long Position, string EventId, string EventType, string Tenant, string RecordTypeId,
    string RecordId, string Version, long Revision, string OperationId, string Principal, string? Executor);

/// <summary>The committed record event types (ADR 0124 decision 2).</summary>
public static class RecordCommittedEvents
{
    /// <summary>A write that created a record.</summary>
    public const string Created = "records.record.created";

    /// <summary>A write that amended an existing record.</summary>
    public const string Updated = "records.record.updated";

    // The pure projection of one committed entry. ADR-0154: the submitter is the principal and the worker its executor.
    internal static RecordCommittedEvent Of(long position, string eventId, RecordWriteRow row, long revision)
        => new(position, eventId, revision > 1 ? Updated : Created, row.Tenant, row.RecordTypeId, row.RecordId, row.Version,
            revision, row.OperationId, row.OriginatorId ?? row.ActorId, row.OriginatorId is null ? null : row.ActorId);
}

/// <summary>
/// The Workflow trigger that reacts to a committed event (ADR 0124 decision 3): the Workflow's key and version plus its
/// trigger id. Two reactions to one source write differ by it; a redelivered event replays the same reaction.
/// </summary>
public sealed record RecordReactionKey(string WorkflowKey, string WorkflowVersion, string TriggerId)
{
    // The operation key's origin frame. Its own frames make it injective, and its prefix keeps it apart from
    // RecordWriteOperationKey.AcceptedCommandOrigin.
    internal string Origin => "reaction:" + RecordWriteOperationKey.Framed(WorkflowKey)
        + RecordWriteOperationKey.Framed(WorkflowVersion) + RecordWriteOperationKey.Framed(TriggerId);
}

/// <summary>A reaction's new record: created from a committed event, never written into the event's source record.</summary>
/// <param name="Source">The committed event the reaction answers; its <c>operation_id</c> is kept (records-run-3).</param>
/// <param name="Reaction">The reacting Workflow trigger.</param>
/// <param name="RecordTypeId">The Record Type of the new record.</param>
/// <param name="Body">The new record's submitted body.</param>
/// <param name="Version">An explicit published version pin, or null for the published head.</param>
public sealed record RecordReactionCreate(RecordCommittedEvent Source, RecordReactionKey Reaction, string RecordTypeId,
    JsonObject Body, string? Version = null);
