using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The authenticated scope the host supplies for a current-record read.</summary>
/// <param name="Tenant">The host's tenant binding; it is never read from the request or body.</param>
/// <param name="ActorId">The authenticated principal whose current read authority the gate decides.</param>
public sealed record RecordReadScope(string Tenant, string ActorId);

/// <summary>A current-row point read, with no caller-selected tenant, version or effective instant.</summary>
/// <param name="RecordId">The exact opaque record identity.</param>
public sealed record RecordReadRequest(string RecordId);

/// <summary>The public projection of one authorized, exactly bound committed row.</summary>
/// <param name="RecordId">The stored record identity.</param>
/// <param name="RecordTypeId">The stored Record Type identity.</param>
/// <param name="Version">The exact published Record Type version pinned by the row.</param>
/// <param name="DefinitionDigest">The verified digest of that published definition.</param>
/// <param name="Schema">The verified registered schema identity pinned by the row.</param>
/// <param name="Body">The row's exact bytes, detached from the store for this result.</param>
/// <param name="RecordedAt">The row's original gate-act instant, not an effective date or an authority-at-commit timestamp.</param>
public sealed record RecordReadResult(string RecordId, string RecordTypeId, string Version, string DefinitionDigest,
    SchemaId Schema, ReadOnlyMemory<byte> Body, DateTimeOffset RecordedAt);

/// <summary>A current read's public projection, or a refusal without record data, and its enforcing decision when available.</summary>
/// <param name="Record">The projection only on success; null for a miss or refusal.</param>
/// <param name="Refusal">Null on success, records.read.not_found for a miss, or the enforcing/binding refusal reason.</param>
/// <param name="Decision">The exact enforcing decision, or null when no decision was made or its context did not match.</param>
/// <remarks>
/// Miss and denied outcomes intentionally remain distinct in this producer. Whether an untrusted host boundary merges
/// their wire representation belongs to that host's disclosure policy, not this storage/read operation. Standings and
/// permitted acts are absent: this bounded slice does not meet records-eng-21 or L061/L062, and enforces no type policy
/// for records-eng-18. It owns no as_of, history, amendment, retention disposal or host endpoint.
/// </remarks>
public sealed record RecordReadOutcome(RecordReadResult? Record, string? Refusal, AuthorizationDecisionEvidence? Decision = null)
{
    /// <summary>True only when this call returned an authorized, bound record projection.</summary>
    public bool Found => Record is not null;
}

/// <summary>Reads one committed row, asks the host's sole gate, then binds the row's exact published type version.</summary>
/// <remarks>
/// The row's version, definition digest and schema identity govern this bounded binding, without a dependency on T-615
/// or a second policy interpreter. The producer and port are released only with the real same-store journal adapter;
/// they do not authorize a host route or integrate independently of that adapter.
/// Current admission can refuse an old published body as schema_unknown. Body-only definition digests and schema
/// identities cannot distinguish identical authored bodies at different published versions; exact version lookup
/// does not independently prove which such version originally produced a row. This producer retains that limitation.
/// </remarks>
public sealed class RecordReadOperation
{
    private const string ReadAct = "records:read";
    private const string NotFound = "records.read.not_found";
    private const string SchemaUnknown = "entity.validation.schema_unknown";
    private readonly IRecordReadStore _records;
    private readonly AccessProvider _access;
    private readonly RecordTypeDefinitionStore _recordTypes;
    private readonly TimeProvider _clock;

    /// <summary>Composes committed storage, the existing authorization provider, the published type binder and host clock.</summary>
    /// <param name="records">The reader over the same committed store the write path uses.</param>
    /// <param name="access">The existing provider over the host's sole authorization decider.</param>
    /// <param name="recordTypes">The tenant-bound published Record Type resolver.</param>
    /// <param name="clock">The authority's time source, sampled once per call.</param>
    public RecordReadOperation(IRecordReadStore records, AccessProvider access, RecordTypeDefinitionStore recordTypes, TimeProvider clock)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Returns a detached authorized current row, a distinct miss, or a refusal carrying the enforcing evidence.</summary>
    /// <param name="scope">The host's authenticated tenant and principal.</param>
    /// <param name="request">The exact record id to look up.</param>
    /// <param name="cancellationToken">Cancellation for lookup, authorization and binding.</param>
    public async ValueTask<RecordReadOutcome> ReadAsync(RecordReadScope scope, RecordReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecordId);
        cancellationToken.ThrowIfCancellationRequested();

        var instant = _clock.GetUtcNow();
        var row = await _records.FindAsync(scope.Tenant, request.RecordId, cancellationToken).ConfigureAwait(false);
        if (row is null) return new(null, NotFound);
        if (!string.Equals(row.Tenant, scope.Tenant, StringComparison.Ordinal)
            || !string.Equals(row.RecordId, request.RecordId, StringComparison.Ordinal))
            throw new InvalidDataException("The record reader returned another tenant or record identity.");

        // Authorization and the result describe one detached snapshot even if a store exposes an array-backed memory.
        var body = row.Body.ToArray();
        var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        try
        {
            if (!System.Text.Unicode.Utf8.IsValid(body))
                throw new InvalidDataException("The committed record body is not a JSON object.");

            var parsed = JsonNode.Parse(body) as JsonObject
                ?? throw new InvalidDataException("The committed record body is not a JSON object.");
            foreach (var member in parsed) fields.Add(member.Key, member.Value?.DeepClone());
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The committed record body is not a JSON object.");
        }
        var access = await _access.CheckWithEvidenceAsync(new AccessRequest(ReadAct, scope.ActorId, scope.Tenant,
            new AccessRecord(scope.Tenant, RecordCreationAuthorization.RecordKind, row.RecordId,
                new ReadOnlyDictionary<string, JsonNode?>(fields)) { RecordTypeId = row.RecordTypeId }, instant),
            cancellationToken).ConfigureAwait(false);
        if (!access.Check.Allowed) return new(null, access.Check.Reason, access.Evidence);
        var evidence = access.Evidence ?? throw new InvalidOperationException("The gate allowed without its evidence.");
        if (row.Version is null)
            throw new InvalidDataException("The committed record has no pinned definition version.");

        RecordTypePublication? publication;
        try
        {
            publication = await _recordTypes.ResolveStoredPublishedSchemaAsync(scope.Tenant, row.RecordTypeId, row.Version, row.Schema,
                cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException)
        {
            publication = null;
        }
        if (publication is null) return new(null, SchemaUnknown, evidence);
        if (!string.Equals(publication.Revision.Digest, row.DefinitionDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The record's definition digest does not match its published version.");
        if (publication.Schema.Id != row.Schema)
            throw new InvalidDataException("The record's schema identity does not match its published version.");

        return new(new(row.RecordId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, body, row.RecordedAt),
            null, evidence);
    }
}
