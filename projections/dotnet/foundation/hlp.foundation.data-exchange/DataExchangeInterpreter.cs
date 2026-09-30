using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Foundation.DataExchange;

/// <summary>A source column name and datatype found during discovery.</summary>
public sealed record DiscoveredSourceColumn(string Name, string Datatype);

/// <summary>The columns a source reports, checked against the mapping before any row is read.</summary>
public sealed record DiscoveredSourceShape(IReadOnlyList<DiscoveredSourceColumn> Columns);

/// <summary>One source row: ordinal, identity, version, the checkpoint boundary after it and its raw string values.</summary>
public sealed record AcquiredSourceRecord(
    int SourceOrdinal,
    string SourceRecordIdentity,
    string SourceRecordVersion,
    string BoundaryAfter,
    IReadOnlyDictionary<string, string?> Values);

/// <summary>A read-only source capability. Acquisition has no write member by construction.</summary>
public interface IReadOnlyAcquisitionSource
{
    /// <summary>Identifier of the acquisition capability.</summary>
    string CapabilityId { get; }

    /// <summary>Version of the connector implementing the capability.</summary>
    string ConnectorVersion { get; }

    /// <summary>Reports the source's columns without reading rows.</summary>
    ValueTask<DiscoveredSourceShape> DiscoverAsync(
        ExchangeSourceBinding binding,
        CancellationToken cancellationToken = default);

    /// <summary>Streams source records from the input boundary onward in source order.</summary>
    IAsyncEnumerable<AcquiredSourceRecord> ReadAsync(
        ExchangeSourceBinding binding,
        string inputBoundary,
        CancellationToken cancellationToken = default);
}

/// <summary>A host-registered value transform that a mapping column names through hl:transform.</summary>
public interface INamedMappingTransform
{
    /// <summary>Returns the transformed value for one coerced column value.</summary>
    object? Apply(object? value);
}

/// <summary>Host-owned capabilities named by an authored definition.</summary>
public interface IDataExchangeCapabilityRegistry
{
    /// <summary>Returns the registered read-only source for the capability and connector version.</summary>
    IReadOnlyAcquisitionSource ResolveSource(string capabilityId, string connectorVersion);

    /// <summary>Returns the registered transform with the given name.</summary>
    INamedMappingTransform ResolveTransform(string name);

    /// <summary>Returns whether the host permits writing the target pointer on the target contract; false becomes mapping.target_forbidden.</summary>
    bool CanWrite(string targetContract, string targetPointer);

    /// <summary>Returns the target's command port for reading recorded outcomes, or null when the contract has no ledger.</summary>
    ICanonicalTargetCommandPort? ResolveTargetLedger(string targetContract);
}

/// <summary>Thrown when a capability or published definition cannot be resolved; Code is the stable code.</summary>
public sealed class DataExchangeCapabilityException(string code) : Exception(code)
{
    /// <summary>Stable refusal code, for example definition.published_head_not_found.</summary>
    public string Code { get; } = code;
}

/// <summary>The mapped column values for one effect, keyed by canonical target pointer, for a target contract.</summary>
public sealed record CanonicalEffectPayload(
    string TargetContract,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>Computes stable payload digests for idempotent effect handling.</summary>
public static class ExchangePayloadDigest
{
    /// <summary>Returns sha256: followed by the lowercase hex digest of the payload's JSON, used to tell replays from conflicts.</summary>
    public static string Compute(CanonicalEffectPayload payload)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
}

/// <summary>Protected store for canonical effect payloads, so run evidence holds only references.</summary>
public interface IProtectedEffectPayloadStore
{
    /// <summary>Stores the payload for the dry run and source ordinal and returns its reference.</summary>
    ValueTask<string> SaveAsync(
        DryRunId dryRunId,
        int sourceOrdinal,
        CanonicalEffectPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the payload for a reference, or null when absent.</summary>
    ValueTask<CanonicalEffectPayload?> GetAsync(
        string reference,
        CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe in-memory payload store for tests and single-process hosts.</summary>
public sealed class InMemoryProtectedEffectPayloadStore : IProtectedEffectPayloadStore
{
    /// <summary>Guards the payload dictionary.</summary>
    private readonly object _gate = new();
    private readonly Dictionary<string, CanonicalEffectPayload> _payloads = [];

    /// <summary>Stores a snapshot under a reference built from dry run id and ordinal; throws ExchangeRunConflictException when that reference already exists.</summary>
    public ValueTask<string> SaveAsync(
        DryRunId dryRunId,
        int sourceOrdinal,
        CanonicalEffectPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reference = $"protected-effect://{dryRunId.Value}/{sourceOrdinal}";
        lock (_gate)
        {
            if (!_payloads.TryAdd(reference, Snapshot(payload)))
            {
                throw new ExchangeRunConflictException($"Protected effect '{reference}' already exists.");
            }
        }
        return ValueTask.FromResult(reference);
    }

    /// <summary>Returns a snapshot copy of the payload for the reference, or null when absent.</summary>
    public ValueTask<CanonicalEffectPayload?> GetAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_payloads.TryGetValue(reference, out var payload)
                ? Snapshot(payload)
                : null);
        }
    }

    private static CanonicalEffectPayload Snapshot(CanonicalEffectPayload payload) => payload with
    {
        Values = payload.Values.ToImmutableDictionary(StringComparer.Ordinal),
    };
}

/// <summary>Caller and source inputs for one dry run: requester, fingerprints, boundaries, snapshot, authorization and retention references, and expected checkpoint.</summary>
public sealed record ExchangeEvaluationContext(
    string RequestedBy,
    string SourceFingerprint,
    string InputBoundary,
    string? SnapshotReference,
    string AuthorizationContextReference,
    string RetentionClass,
    string DependencyFingerprint = "none",
    string TransformVersionsFingerprint = "none",
    string LookupVersionsFingerprint = "none",
    string MatchingInputsFingerprint = "none",
    string? SelectedBoundary = null,
    string? ExpectedCheckpoint = null);

/// <summary>Turns a published definition and a read-only source into a dry run: discovers the shape, maps rows, classifies each against the target ledger and records review evidence.</summary>
public sealed class DataExchangeInterpreter(
    IDataExchangeCapabilityRegistry capabilities,
    DataExchangeRuntime runtime,
    IProtectedEffectPayloadStore payloads)
{
    /// <summary>Host registry of sources, transforms, writable targets and target ledgers.</summary>
    private readonly IDataExchangeCapabilityRegistry _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    /// <summary>Runtime that persists the dry-run evidence.</summary>
    private readonly DataExchangeRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    /// <summary>Protected store the mapped payloads are saved to.</summary>
    private readonly IProtectedEffectPayloadStore _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));

    /// <summary>Resolves the published head, then creates its dry run; throws DataExchangeCapabilityException definition.published_head_not_found when none exists.</summary>
    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        IDataExchangeDefinitionResolver definitions,
        string tenant,
        string definitionKey,
        ExchangeEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var definition = await definitions.ResolvePublishedHeadAsync(tenant, definitionKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new DataExchangeCapabilityException("definition.published_head_not_found");
        return await CreateDryRunAsync(definition, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a dry run: unmappable rows become Rejected mapping refusals, mapped rows are classified against the ledger, and evidence is persisted without writing target records; throws DataExchangeAdmissionException when a mapped column is missing from the source.</summary>
    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        DataExchangeDefinition definition,
        ExchangeEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        var mapping = TabularMappingAdmission.Validate(definition.Mapping);
        var source = _capabilities.ResolveSource(definition.Source.CapabilityId, definition.Source.ConnectorVersion);
        var shape = await source.DiscoverAsync(definition.Source, cancellationToken).ConfigureAwait(false);
        ValidateShape(mapping, shape);

        var mappingDigest = Digest(mapping);
        var proposal = new ProposalFingerprint(
            context.SourceFingerprint,
            context.InputBoundary,
            definition.Key,
            definition.Version,
            definition.Key + ":mapping",
            mapping.Version,
            mappingDigest,
            source.CapabilityId,
            source.ConnectorVersion,
            mapping.Target.Contract,
            context.DependencyFingerprint,
            mapping.Profile,
            context.TransformVersionsFingerprint,
            context.LookupVersionsFingerprint,
            context.MatchingInputsFingerprint,
            context.SelectedBoundary ?? context.InputBoundary);
        var batchIdentity = ExchangeIdentity.DeriveBatch(BatchIdentityInputs.From(definition.Tenant, proposal));
        var ledger = _capabilities.ResolveTargetLedger(mapping.Target.Contract);
        var dryRunId = DryRunId.New();
        var effects = new List<ProposedEffect>();
        var evaluations = new List<DryRunEffectEvaluation>();
        var candidateCheckpoint = string.Empty;
        await foreach (var row in source.ReadAsync(definition.Source, context.InputBoundary, cancellationToken)
            .ConfigureAwait(false))
        {
            candidateCheckpoint = row.BoundaryAfter;
            try
            {
                var payload = Map(mapping, row);
                var externalIdentity = ExternalIdentity(definition, payload, row.SourceRecordIdentity);
                var payloadDigest = ExchangePayloadDigest.Compute(payload);
                var payloadReference = await _payloads.SaveAsync(
                    dryRunId,
                    row.SourceOrdinal,
                    payload,
                    cancellationToken).ConfigureAwait(false);
                var effect = new ProposedEffect(
                    row.SourceOrdinal,
                    externalIdentity,
                    row.SourceRecordVersion,
                    "canonical-record",
                    row.BoundaryAfter,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["payloadDigest"] = payloadDigest,
                        ["targetContract"] = mapping.Target.Contract,
                    },
                    payloadReference);
                var effectIdentity = ExchangeIdentity.DeriveEffect(
                    batchIdentity, mapping.Target.Contract, externalIdentity, row.SourceRecordVersion, "canonical-record");
                EffectTerminalOutcome outcome;
                try
                {
                    var existing = ledger is null
                        ? null
                        : await ledger.GetOutcomeAsync(effectIdentity, cancellationToken).ConfigureAwait(false);
                    if (existing is not null)
                    {
                        // Refuse rather than silently reclassify a corrupt or foreign ledger row
                        // (spec review c3): an unrecognized status, or an entry keyed to a
                        // different batch/effect than the one just looked up, is not evidence
                        // this dry run may reason about.
                        ExchangeRunClosure.ValidateOutcome(existing.Outcome);
                        if (existing.BatchIdentity != batchIdentity || existing.EffectIdentity != effectIdentity)
                        {
                            throw new DataExchangeCommitRefusedException(
                                "run.ledger_mismatch", "The recorded outcome belongs to different semantic intent.");
                        }
                    }
                    outcome = Classify(existing, payloadDigest);
                }
                catch (Exception exception) when (exception is not DataExchangeCommitRefusedException)
                {
                    // Only a failure of the read-only ledger lookup itself is downgraded to a
                    // per-row Failed outcome so the batch continues (eng-10). A refusal raised by
                    // Map/payload storage (e.g. FieldAdmissionException) is a fail-closed boundary
                    // and must keep propagating, never be silently swallowed into a Failed row.
                    outcome = new(ExchangeEffectStatus.Failed, "run.effect_evaluation_failed");
                }
                if (outcome.Status == ExchangeEffectStatus.Applied)
                {
                    effects.Add(effect);
                }
                evaluations.Add(new(effect, outcome));
            }
            catch (MappingRowException exception)
            {
                var rejected = new ProposedEffect(
                    row.SourceOrdinal,
                    row.SourceRecordIdentity,
                    row.SourceRecordVersion,
                    "mapping-refusal",
                    row.BoundaryAfter,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["refusalPointer"] = exception.Pointer,
                    });
                evaluations.Add(new(rejected, new(ExchangeEffectStatus.Rejected, exception.Code)));
            }
        }

        return await _runtime.CreateDryRunAsync(
            new DryRunRequest(
                TenantId: definition.Tenant,
                RequestedBy: context.RequestedBy,
                Proposal: proposal,
                Effects: effects,
                CandidateCheckpoint: candidateCheckpoint,
                SnapshotReference: context.SnapshotReference,
                AuthorizationContextReference: context.AuthorizationContextReference,
                RetentionClass: context.RetentionClass,
                Evaluations: evaluations,
                PrescribedId: dryRunId,
                ExpectedCheckpoint: context.ExpectedCheckpoint),
            cancellationToken).ConfigureAwait(false);
    }

    private static EffectTerminalOutcome Classify(EffectLedgerEntry? existing, string payloadDigest)
    {
        if (existing is null)
        {
            return new(ExchangeEffectStatus.Applied, "mapping.proposed");
        }
        if (existing.Outcome.Status is ExchangeEffectStatus.Applied or ExchangeEffectStatus.Skipped)
        {
            return StringComparer.Ordinal.Equals(existing.PayloadDigest, payloadDigest)
                ? new(ExchangeEffectStatus.Skipped, "replay.already_applied")
                : new(ExchangeEffectStatus.Conflicted, "replay.effect_conflict");
        }
        // A prior Conflicted, Rejected, Failed or Halted outcome is already one of the six closed
        // arms (eng-8) and already carries the correction/retry/acknowledgement semantics
        // CommitContracts assigns per status (spec review c2). Reporting anything other than that
        // recorded outcome, verbatim, would erase that meaning rather than merely re-observe it.
        return existing.Outcome;
    }

    private static void ValidateShape(TabularMappingDocument mapping, DiscoveredSourceShape shape)
    {
        var discovered = shape.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
        var missing = mapping.Columns.Where(column => !discovered.Contains(column.Name)).ToArray();
        if (missing.Length > 0)
        {
            throw new DataExchangeAdmissionException(
                missing.Select(column => new DataExchangeRefusal("mapping.source_column_unknown", $"/columns/{column.Name}"))
                    .ToArray());
        }
    }

    private CanonicalEffectPayload Map(TabularMappingDocument mapping, AcquiredSourceRecord row)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var column in mapping.Columns)
        {
            row.Values.TryGetValue(column.Name, out var raw);
            if (column.Null?.Contains(raw ?? string.Empty, StringComparer.Ordinal) == true)
            {
                raw = null;
            }
            if (string.IsNullOrEmpty(raw) && column.Default is not null)
            {
                raw = column.Default;
            }
            if (raw is null && column.Required)
            {
                throw new MappingRowException("mapping.required_missing", $"/columns/{column.Name}");
            }
            if (!_capabilities.CanWrite(mapping.Target.Contract, column.Target))
            {
                throw new MappingRowException("mapping.target_forbidden", $"/columns/{column.Name}/target");
            }
            var coerced = Coerce(column.Datatype, raw, column.Separator, column.Name);
            if (column.Extensions?.TryGetValue("hl:transform", out var transformName) == true)
            {
                coerced = _capabilities.ResolveTransform(transformName).Apply(coerced);
            }
            values[column.Target] = coerced;
        }
        return new(mapping.Target.Contract, values);
    }

    private static object? Coerce(string datatype, string? raw, string? separator, string column)
    {
        if (raw is null)
        {
            return null;
        }
        if (separator is not null)
        {
            return raw.Split(separator, StringSplitOptions.None)
                .Select(value => Coerce(datatype, value, null, column))
                .ToArray();
        }
        try
        {
            return datatype switch
            {
                "string" => raw,
                "decimal" => decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture),
                "integer" => long.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "boolean" => bool.Parse(raw),
                "date" => DateOnly.Parse(raw, CultureInfo.InvariantCulture),
                "dateTime" => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                _ => throw new MappingRowException("mapping.datatype_unsupported", $"/columns/{column}/datatype"),
            };
        }
        catch (MappingRowException)
        {
            throw;
        }
        catch (FormatException)
        {
            throw new MappingRowException("mapping.datatype_invalid", $"/columns/{column}");
        }
        catch (OverflowException)
        {
            throw new MappingRowException("mapping.datatype_invalid", $"/columns/{column}");
        }
    }

    private static string ExternalIdentity(
        DataExchangeDefinition definition,
        CanonicalEffectPayload payload,
        string fallback)
    {
        if (definition.ExternalKeyColumns.Count == 0)
        {
            return fallback;
        }
        var targets = definition.Mapping.Columns.ToDictionary(column => column.Name, column => column.Target, StringComparer.Ordinal);
        // A composite external key joins with the ASCII unit separator, not '|': ExchangeIdentity's
        // own Join() refuses any identity component that contains '|' (its own canonical
        // separator), so a two-plus-column key joined with '|' aborted every row that reached
        // ExchangeIdentity.DeriveEffect (spec review c1). U+001F cannot appear in ordinary column
        // values and is never forbidden by that check.
        return string.Join('\u001f', definition.ExternalKeyColumns.Select(column =>
            $"{column}={Convert.ToString(payload.Values[targets[column]], CultureInfo.InvariantCulture)}"));
    }

    private static string Digest(TabularMappingDocument mapping)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TabularMappingJson.Serialize(mapping))));

    private sealed class MappingRowException(string code, string pointer) : Exception(code)
    {
        /// <summary>Stable mapping refusal code, for example mapping.required_missing.</summary>
        public string Code { get; } = code;

        /// <summary>JSON pointer of the refused column.</summary>
        public string Pointer { get; } = pointer;
    }
}
