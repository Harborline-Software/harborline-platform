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

/// <summary>A person-selected business-effective point; it is not an authority-known-at query.</summary>
/// <param name="RecordId">Exact logical record identity.</param>
/// <param name="AsOf">Explicit effective instant, never inferred from a clock.</param>
public sealed record RecordTemporalReadRequest(string RecordId, DateTimeOffset AsOf);
/// <summary>The authorized seven-field public row plus bounded temporal evidence.</summary>
/// <param name="Record">Old public record projection.</param>
/// <param name="Temporal">Accepted interval, reason and causal evidence.</param>
/// <param name="BindingHash">Local temporal integrity hash.</param>
public sealed record RecordTemporalReadResult(RecordReadResult Record, RecordTemporalEvidence Temporal, string BindingHash);
/// <summary>Explicit effective-read states, without a new error catalogue.</summary>
public enum RecordTemporalReadStatus
{
    /// <summary>Complete authorized and bound data.</summary>
    Found = 0,
    /// <summary>Existing enforcing or binding refusal.</summary>
    Refused = 1,
    /// <summary>No exact logical identity.</summary>
    NotFound = 2,
    /// <summary>No revision covers the effective instant.</summary>
    NoEffectiveRevision = 3,
    /// <summary>An old ordinary row has no temporal provenance.</summary>
    UnsupportedLegacyBasis = 4,
    /// <summary>The composed store has no temporal read port.</summary>
    TemporalStoreUnavailable = 5,
}
/// <summary>One explicit effective read.</summary>
/// <param name="Status">Selection and enforcement state.</param>
/// <param name="Record">Complete data only on Found.</param>
/// <param name="Refusal">Existing reason only on Refused.</param>
/// <param name="Decision">Current enforcing decision when available.</param>
public sealed record RecordTemporalReadOutcome(RecordTemporalReadStatus Status, RecordTemporalReadResult? Record,
    string? Refusal, AuthorizationDecisionEvidence? Decision = null);
/// <summary>All-or-nothing current authorization of complete causal history.</summary>
/// <param name="Status">Complete success or typed state.</param>
/// <param name="Records">Complete authorized causal sequence, never a prefix.</param>
/// <param name="Refusal">Existing reason only on refusal.</param>
/// <param name="Decisions">Success decisions in causal order, or only the enforcing refusal decision.</param>
public sealed record RecordTemporalHistoryOutcome(RecordTemporalReadStatus Status, IReadOnlyList<RecordTemporalReadResult> Records,
    string? Refusal, IReadOnlyList<AuthorizationDecisionEvidence> Decisions);

/// <summary>Reads one committed row, asks the host's sole gate, then binds the row's exact published type version.</summary>
/// <remarks>
/// The row's version, definition digest and schema identity govern this bounded binding, without a dependency on T-615
/// or a second policy interpreter. The producer and port are released only with the real same-store journal adapter;
/// they do not authorize a host route or integrate independently of that adapter.
/// Current admission can refuse an old published body as schema_unknown. Body-only definition digests and schema
/// identities cannot distinguish identical authored bodies at different published versions on legacy rows. Bound rows
/// select the recorded immutable PublicationVersionId and verify its tenant/type, label, exact body digest and schema.
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

    /// <summary>Selects by explicit business time and enforces current authority on that revision.</summary>
    public async ValueTask<RecordTemporalReadOutcome> ReadAtAsync(RecordReadScope scope,
        RecordTemporalReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReadArguments(scope, request.RecordId, cancellationToken);
        if (_records is not IRecordTemporalReadStore temporal)
            return new(RecordTemporalReadStatus.TemporalStoreUnavailable, null, null);
        var instant = _clock.GetUtcNow();
        var lookup = await temporal.FindAtAsync(scope.Tenant, request.RecordId, request.AsOf, cancellationToken).ConfigureAwait(false);
        ValidateLookup(lookup);
        if (lookup.Kind != RecordTemporalLookupKind.Temporal)
            return new(lookup.Kind switch
            {
                RecordTemporalLookupKind.NotFound => RecordTemporalReadStatus.NotFound,
                RecordTemporalLookupKind.NonTemporal => RecordTemporalReadStatus.UnsupportedLegacyBasis,
                RecordTemporalLookupKind.NoEffectiveRevision => RecordTemporalReadStatus.NoEffectiveRevision,
                _ => throw new InvalidOperationException("The temporal lookup state is unknown."),
            }, null, null);
        var version = FreezeVersion(lookup.Temporal!, scope.Tenant, request.RecordId);
        var access = await AuthorizeTemporalAsync(scope, version.Row, instant, cancellationToken).ConfigureAwait(false);
        if (access.Refusal is not null) return new(RecordTemporalReadStatus.Refused, null, access.Refusal, access.Decision);
        var record = await BindTemporalRowAsync(scope.Tenant, version.Row, access.Body, cancellationToken).ConfigureAwait(false);
        return record is null ? new(RecordTemporalReadStatus.Refused, null, SchemaUnknown, access.Decision)
            : new(RecordTemporalReadStatus.Found, new(record, version.Temporal, version.BindingHash), null, access.Decision);
    }

    /// <summary>Returns complete causal history only after every current gate and immutable binding succeeds.</summary>
    public async ValueTask<RecordTemporalHistoryOutcome> ReadHistoryAsync(RecordReadScope scope, string recordId,
        CancellationToken cancellationToken = default)
    {
        ReadArguments(scope, recordId, cancellationToken);
        if (_records is not IRecordTemporalReadStore temporal)
            return EmptyHistory(RecordTemporalReadStatus.TemporalStoreUnavailable);
        var instant = _clock.GetUtcNow();
        var history = await temporal.HistoryAsync(scope.Tenant, recordId, cancellationToken).ConfigureAwait(false);
        if (history?.Versions is null) throw new InvalidOperationException("The temporal history state is invalid.");
        if (history.Kind is RecordTemporalHistoryKind.NotFound or RecordTemporalHistoryKind.NonTemporal)
        {
            if (history.Versions.Count != 0) throw new InvalidOperationException("A non-temporal result contains history.");
            return EmptyHistory(history.Kind == RecordTemporalHistoryKind.NotFound
                ? RecordTemporalReadStatus.NotFound : RecordTemporalReadStatus.UnsupportedLegacyBasis);
        }
        if (history.Kind != RecordTemporalHistoryKind.Temporal || history.Versions.Count == 0)
            throw new InvalidOperationException("The temporal history state is unknown or empty.");
        var versions = history.Versions.ToArray().Select(version => FreezeVersion(version, scope.Tenant, recordId)).ToArray();
        for (var i = 0; i < versions.Length; i++)
            if (versions[i].Temporal.Revision != i + 1L
                || i > 0 && (versions[i].Temporal.Predecessor != new RecordTemporalBasis(versions[i - 1].Temporal.Revision,
                    versions[i - 1].BindingHash) || versions[i].Row.RecordTypeId != versions[0].Row.RecordTypeId))
                throw new InvalidOperationException("The temporal history is not a causal sequence.");
        var decisions = new List<AuthorizationDecisionEvidence>();
        var bodies = new List<byte[]>();
        foreach (var version in versions)
        {
            var access = await AuthorizeTemporalAsync(scope, version.Row, instant, cancellationToken).ConfigureAwait(false);
            if (access.Refusal is not null)
                return EmptyHistory(RecordTemporalReadStatus.Refused, access.Refusal, access.Decision);
            decisions.Add(access.Decision!);
            bodies.Add(access.Body);
        }
        var records = new List<RecordTemporalReadResult>();
        for (var i = 0; i < versions.Length; i++)
        {
            var record = await BindTemporalRowAsync(scope.Tenant, versions[i].Row, bodies[i], cancellationToken).ConfigureAwait(false);
            if (record is null) return EmptyHistory(RecordTemporalReadStatus.Refused, SchemaUnknown, decisions[i]);
            records.Add(new(record, versions[i].Temporal, versions[i].BindingHash));
        }
        return new(RecordTemporalReadStatus.Found, Array.AsReadOnly(records.ToArray()), null, Array.AsReadOnly(decisions.ToArray()));
    }

    private static void ReadArguments(RecordReadScope scope, string recordId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ct.ThrowIfCancellationRequested();
    }

    private static RecordWriteRow? CurrentSelection(RecordTemporalLookup lookup)
    {
        ValidateLookup(lookup);
        return lookup.Kind switch
        {
            RecordTemporalLookupKind.NonTemporal => lookup.NonTemporal,
            RecordTemporalLookupKind.Temporal => lookup.Temporal!.Row,
            _ => null,
        };
    }

    private static void ValidateLookup(RecordTemporalLookup? value)
    {
        if (value is null || (value.Kind switch
        {
            RecordTemporalLookupKind.NonTemporal => value.NonTemporal is null || value.Temporal is not null,
            RecordTemporalLookupKind.Temporal => value.Temporal is null || value.NonTemporal is not null,
            RecordTemporalLookupKind.NotFound or RecordTemporalLookupKind.NoEffectiveRevision => value.NonTemporal is not null || value.Temporal is not null,
            _ => true,
        })) throw new InvalidOperationException("The temporal lookup state is inconsistent.");
    }

    private static RecordTemporalHistoryOutcome EmptyHistory(RecordTemporalReadStatus status, string? refusal = null,
        AuthorizationDecisionEvidence? decision = null)
        => new(status, Array.Empty<RecordTemporalReadResult>(), refusal,
            decision is null ? Array.Empty<AuthorizationDecisionEvidence>() : Array.AsReadOnly(new[] { decision }));

    private static RecordTemporalVersion FreezeVersion(RecordTemporalVersion value, string tenant, string recordId)
    {
        if (value?.Row is not { } row || value.Temporal is not { } evidence || evidence.Supersedes is null
            || !RecordWriteJournalBinding.IsHash(value.BindingHash) || evidence.Revision <= 0
            || evidence.EffectiveTo is { } end && end.UtcTicks <= evidence.EffectiveFrom.UtcTicks
            || row.PublicationVersionId is null)
            throw new InvalidOperationException("The temporal version is invalid.");
        if (row.Tenant != tenant || row.RecordId != recordId)
            throw new InvalidDataException("The record reader returned another tenant or record identity.");
        if (evidence.Revision == 1
            ? evidence.Predecessor is not null || evidence.Reason is not null || evidence.Supersedes.Count != 0
            : evidence.Predecessor is not { } predecessor || predecessor.Revision != evidence.Revision - 1
                || !RecordWriteJournalBinding.IsHash(predecessor.Hash) || evidence.Reason is null)
            throw new InvalidOperationException("The temporal evidence is inconsistent.");
        var segments = evidence.Supersedes.Select(segment =>
        {
            if (segment?.Revision is not { } basis || basis.Revision <= 0 || basis.Revision >= evidence.Revision
                || !RecordWriteJournalBinding.IsHash(basis.Hash) || segment.EffectiveFrom.Offset != TimeSpan.Zero
                || segment.EffectiveTo is { } to && (to.Offset != TimeSpan.Zero || to.UtcTicks <= segment.EffectiveFrom.UtcTicks))
                throw new InvalidOperationException("The temporal segment is invalid.");
            return new RecordTemporalSupersededSegment(new(basis.Revision, basis.Hash), segment.EffectiveFrom, segment.EffectiveTo);
        }).ToArray();
        return new(row with { Body = row.Body.ToArray() }, evidence with
        {
            Predecessor = evidence.Predecessor is null ? null : new(evidence.Predecessor.Revision, evidence.Predecessor.Hash),
            Supersedes = Array.AsReadOnly(segments),
        }, value.BindingHash);
    }

    private async ValueTask<(byte[] Body, AuthorizationDecisionEvidence? Decision, string? Refusal)> AuthorizeTemporalAsync(
        RecordReadScope scope, RecordWriteRow row, DateTimeOffset instant, CancellationToken ct)
    {
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
        { throw new InvalidDataException("The committed record body is not a JSON object."); }
        var access = await _access.CheckWithEvidenceAsync(new AccessRequest(ReadAct, scope.ActorId, scope.Tenant,
            new AccessRecord(scope.Tenant, RecordCreationAuthorization.RecordKind, row.RecordId,
                new ReadOnlyDictionary<string, JsonNode?>(fields)) { RecordTypeId = row.RecordTypeId }, instant), ct).ConfigureAwait(false);
        if (!access.Check.Allowed) return (body, access.Evidence, access.Check.Reason);
        return (body, access.Evidence ?? throw new InvalidOperationException("The gate allowed without its evidence."), null);
    }

    private async ValueTask<RecordReadResult?> BindTemporalRowAsync(string tenant, RecordWriteRow row, byte[] body, CancellationToken ct)
    {
        if (row.Version is null) throw new InvalidDataException("The committed record has no pinned definition version.");
        try { _ = RecordWriteJournalBinding.Encode(row); }
        catch (InvalidOperationException) { throw new InvalidDataException("The committed record publication binding is invalid."); }
        RecordTypePublication? publication;
        try
        {
            publication = await _recordTypes.ResolveBoundStoredPublishedSchemaAsync(tenant, row.RecordTypeId,
                row.PublicationVersionId!, row.Version, row.Schema, ct).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException) { publication = null; }
        if (publication is null) return null;
        try { RecordWriteJournalBinding.ValidatePublication(row, publication); }
        catch (InvalidOperationException)
        { throw new InvalidDataException("The record's publication evidence does not match its published version."); }
        if (!string.Equals(publication.Revision.Digest, row.DefinitionDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The record's definition digest does not match its published version.");
        if (publication.Schema.Id != row.Schema)
            throw new InvalidDataException("The record's schema identity does not match its published version.");
        return new(row.RecordId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, body, row.RecordedAt);
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
        var row = _records is IRecordTemporalReadStore temporal
            ? CurrentSelection(await temporal.FindAtAsync(scope.Tenant, request.RecordId, instant, cancellationToken).ConfigureAwait(false))
            : await _records.FindAsync(scope.Tenant, request.RecordId, cancellationToken).ConfigureAwait(false);
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
        if (row.PublicationVersionId is not null)
        {
            try { _ = RecordWriteJournalBinding.Encode(row); }
            catch (InvalidOperationException)
            { throw new InvalidDataException("The committed record publication binding is invalid."); }
        }

        RecordTypePublication? publication;
        try
        {
            publication = row.PublicationVersionId is { } versionId
                ? await _recordTypes.ResolveBoundStoredPublishedSchemaAsync(scope.Tenant, row.RecordTypeId, versionId, row.Version,
                    row.Schema, cancellationToken).ConfigureAwait(false)
                : await _recordTypes.ResolveStoredPublishedSchemaAsync(scope.Tenant, row.RecordTypeId, row.Version, row.Schema,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (DefinitionRefusalException)
        {
            publication = null;
        }
        if (publication is null) return new(null, SchemaUnknown, evidence);
        if (row.PublicationVersionId is not null)
        {
            try { RecordWriteJournalBinding.ValidatePublication(row, publication); }
            catch (InvalidOperationException)
            { throw new InvalidDataException("The record's publication evidence does not match its published version."); }
        }
        if (!string.Equals(publication.Revision.Digest, row.DefinitionDigest, StringComparison.Ordinal))
            throw new InvalidDataException("The record's definition digest does not match its published version.");
        if (publication.Schema.Id != row.Schema)
            throw new InvalidDataException("The record's schema identity does not match its published version.");

        return new(new(row.RecordId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, body, row.RecordedAt),
            null, evidence);
    }
}
