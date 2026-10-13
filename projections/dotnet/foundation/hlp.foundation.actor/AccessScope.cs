using System.Text.Json.Nodes;

namespace Harborline.Foundation.Authorization;

/// <summary>Record facts supplied by the tenant-bound record reader, never ambient state.</summary>
public sealed record AccessRecord(string Tenant, string Kind, string Id, IReadOnlyDictionary<string, JsonNode?> Fields)
{
    /// <summary>
    /// The typed Records record-type target (plan W9, revision 5): the Record Type a record of <see cref="Kind"/> belongs
    /// to, or is about to be created as, so the decider can decide an act on one type (for example the type's creation
    /// role, decision D1) rather than on an untyped record. Null for an untyped target, which every existing request is.
    /// When present it is nonblank, compared ordinally and never trimmed; the decider's evidence must name the same type.
    /// </summary>
    public string? RecordTypeId { get; init; }
}

/// <summary>The complete point-of-use request forwarded to the host's existing authorization gate.</summary>
public sealed record AccessRequest(string Operation, string Principal, string Tenant, AccessRecord Record, DateTimeOffset At);

/// <summary>A scope or row-check result with a stable public reason.</summary>
public sealed record AccessCheck(bool Allowed, string Reason);
