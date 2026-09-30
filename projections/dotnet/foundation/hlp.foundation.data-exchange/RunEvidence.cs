using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.DataExchange;

/// <summary>The semantic inputs that identify a proposal: source, boundary, definition, mapping, connector, target, dependency and matching fingerprints.</summary>
public sealed record ProposalFingerprint(
    string SourceFingerprint,
    string InputBoundary,
    string DefinitionId,
    string DefinitionVersion,
    string MappingId,
    string MappingVersion,
    string MappingDigest,
    string ConnectorId,
    string ConnectorVersion,
    string TargetContract,
    string DependencyFingerprint,
    string MappingProfile,
    string TransformVersionsFingerprint,
    string LookupVersionsFingerprint,
    string MatchingInputsFingerprint,
    string SelectedBoundary);

/// <summary>Normalized non-sensitive evidence for one intended canonical target effect.</summary>
public sealed record ProposedEffect(
    int SourceOrdinal,
    string SourceRecordIdentity,
    string SourceRecordVersion,
    string EffectDiscriminator,
    string BoundaryAfter,
    IReadOnlyDictionary<string, string> Metadata,
    string? PayloadReference = null);

/// <summary>One reviewed effect with its predicted terminal outcome.</summary>
public sealed record DryRunEffectEvaluation(
    ProposedEffect Effect,
    EffectTerminalOutcome Outcome);

/// <summary>Request for a dry run: tenant, requester, proposal, candidate effects or precomputed evaluations, and checkpoint references.</summary>
/// <param name="TenantId">Tenant that owns the proposed exchange.</param>
/// <param name="RequestedBy">Actor requesting the review.</param>
/// <param name="Proposal">Source and target proposal being evaluated.</param>
/// <param name="Effects">Candidate effects when no evaluations are supplied.</param>
/// <param name="CandidateCheckpoint">Checkpoint boundary proposed after the effect window.</param>
/// <param name="SnapshotReference">Reference to the source snapshot used for review.</param>
/// <param name="AuthorizationContextReference">Reference to the authorization context captured for review.</param>
/// <param name="RetentionClass">Retention policy class assigned to the run.</param>
/// <param name="Evaluations">Optional precomputed effect evaluations.</param>
/// <param name="PrescribedId">Optional caller-supplied dry-run identity.</param>
/// <param name="ExpectedCheckpoint">Checkpoint value expected before promotion.</param>
/// <param name="SupersedesDryRunId">Prior dry run replaced by this review, when applicable.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DryRunRequest(
    string TenantId,
    string RequestedBy,
    ProposalFingerprint Proposal,
    IReadOnlyList<ProposedEffect> Effects,
    string CandidateCheckpoint,
    string? SnapshotReference,
    string AuthorizationContextReference,
    string RetentionClass,
    IReadOnlyList<DryRunEffectEvaluation>? Evaluations = null,
    DryRunId? PrescribedId = null,
    string? ExpectedCheckpoint = null,
    DryRunId? SupersedesDryRunId = null);

/// <summary>An immutable tenant-scoped evaluation artifact; sensitive payloads remain referenced.</summary>
public sealed record DryRunArtifact(
    DryRunId Id,
    string TenantId,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    ProposalFingerprint Proposal,
    IReadOnlyList<ProposedEffect> NormalizedEffects,
    string CandidateCheckpoint,
    string? SnapshotReference,
    string AuthorizationContextReference,
    string RetentionClass,
    DateTimeOffset RetainUntil,
    IReadOnlyList<DryRunEffectEvaluation> Evaluations,
    ExchangeCensus Census,
    string? ExpectedCheckpoint,
    bool LegalHold = false,
    DryRunId? SupersedesDryRunId = null);

/// <summary>Whether a commit run's checkpoint promotion has completed.</summary>
public enum CheckpointFinalizationStatus
{
    /// <summary>The commit run is saved but its checkpoint is not yet promoted.</summary>
    Pending,
    /// <summary>The checkpoint has been promoted, or there was nothing to promote.</summary>
    Finalized,
}

/// <summary>Records the expected and promoted checkpoint of a commit run and whether promotion has finished.</summary>
public sealed record CommitCheckpointFinalization(
    CommitRunId CommitRunId,
    string? ExpectedCheckpoint,
    string? PromotedCheckpoint,
    CheckpointFinalizationStatus Status,
    DateTimeOffset RecordedAt);

/// <summary>Thrown when a run, protected effect or final checkpoint record already exists.</summary>
public sealed class ExchangeRunConflictException(string message) : Exception(message);

/// <summary>Durable store for dry-run evidence, commit-run evidence and checkpoint finalizations.</summary>
public interface IExchangeRunStore
{
    /// <summary>Saves the dry run; a duplicate id is refused with ExchangeRunConflictException.</summary>
    ValueTask SaveDryRunAsync(DryRunArtifact artifact, CancellationToken cancellationToken = default);
    /// <summary>Returns the dry run, or null when absent.</summary>
    ValueTask<DryRunArtifact?> GetDryRunAsync(DryRunId id, CancellationToken cancellationToken = default);
    /// <summary>Saves the commit run; a duplicate id is refused.</summary>
    ValueTask SaveCommitRunAsync(CommitRunArtifact artifact, CancellationToken cancellationToken = default);
    /// <summary>Stores a commit run together with its checkpoint finalization as one durable record.</summary>
    ValueTask SaveCommitRunWithCheckpointFinalizationAsync(
        CommitRunArtifact artifact,
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default);
    /// <summary>Returns the commit run, or null when absent.</summary>
    ValueTask<CommitRunArtifact?> GetCommitRunAsync(CommitRunId id, CancellationToken cancellationToken = default);
    /// <summary>Lists immutable run evidence associated with the supplied dry run.</summary>
    ValueTask<IReadOnlyList<CommitRunArtifact>> ListCommitRunsAsync(DryRunId approvedDryRunId, CancellationToken cancellationToken = default);
    /// <summary>Saves the finalization; replacing one that is already Finalized is refused.</summary>
    ValueTask SaveCheckpointFinalizationAsync(CommitCheckpointFinalization finalization, CancellationToken cancellationToken = default);
    /// <summary>Returns the finalization for the commit run, or null when absent.</summary>
    ValueTask<CommitCheckpointFinalization?> GetCheckpointFinalizationAsync(CommitRunId id, CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe in-memory run store that keeps snapshot copies; for tests and single-process hosts.</summary>
public sealed class InMemoryExchangeRunStore : IExchangeRunStore
{
    /// <summary>Guards the run and finalization dictionaries.</summary>
    private readonly object _gate = new();
    private readonly Dictionary<DryRunId, DryRunArtifact> _dryRuns = [];
    private readonly Dictionary<CommitRunId, CommitRunArtifact> _commitRuns = [];
    private readonly Dictionary<CommitRunId, CommitCheckpointFinalization> _checkpointFinalizations = [];

    /// <summary>Stores a snapshot; throws ExchangeRunConflictException when the id already exists.</summary>
    public ValueTask SaveDryRunAsync(
        DryRunArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_dryRuns.TryAdd(artifact.Id, Snapshot(artifact)))
            {
                throw new ExchangeRunConflictException($"Dry run '{artifact.Id.Value}' already exists.");
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns a snapshot of the dry run, or null when absent.</summary>
    public ValueTask<DryRunArtifact?> GetDryRunAsync(
        DryRunId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_dryRuns.TryGetValue(id, out var artifact)
                ? Snapshot(artifact)
                : null);
        }
    }

    /// <summary>Validates the run against its approved dry run (run.not_found when missing), then stores a snapshot; throws ExchangeRunConflictException on a duplicate id.</summary>
    public ValueTask SaveCommitRunAsync(
        CommitRunArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_dryRuns.TryGetValue(artifact.ApprovedDryRunId, out var review))
            {
                throw new DataExchangeCommitRefusedException("run.not_found", "The approved dry run does not exist.");
            }
            ExchangeRunClosure.Validate(review, artifact);
            if (!_commitRuns.TryAdd(artifact.Id, Snapshot(artifact)))
            {
                throw new ExchangeRunConflictException($"Commit run '{artifact.Id.Value}' already exists.");
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Stores the run and its finalization together or not at all; throws ArgumentException when the finalization belongs to another run and ExchangeRunConflictException when either already exists.</summary>
    public ValueTask SaveCommitRunWithCheckpointFinalizationAsync(
        CommitRunArtifact artifact,
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(finalization);
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.Id != finalization.CommitRunId)
        {
            throw new ArgumentException("The checkpoint finalization must belong to the commit run.", nameof(finalization));
        }
        lock (_gate)
        {
            if (_commitRuns.ContainsKey(artifact.Id) || _checkpointFinalizations.ContainsKey(artifact.Id))
            {
                throw new ExchangeRunConflictException($"Commit run '{artifact.Id.Value}' already exists.");
            }
            _commitRuns.Add(artifact.Id, Snapshot(artifact));
            _checkpointFinalizations.Add(artifact.Id, finalization with { });
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns a snapshot of the commit run, or null when absent.</summary>
    public ValueTask<CommitRunArtifact?> GetCommitRunAsync(
        CommitRunId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_commitRuns.TryGetValue(id, out var artifact)
                ? Snapshot(artifact)
                : null);
        }
    }

    /// <summary>Returns snapshots of the commit runs approved from the given dry run.</summary>
    public ValueTask<IReadOnlyList<CommitRunArtifact>> ListCommitRunsAsync(
        DryRunId approvedDryRunId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<CommitRunArtifact>>(_commitRuns.Values
                .Where(run => run.ApprovedDryRunId == approvedDryRunId)
                .Select(Snapshot)
                .ToArray());
        }
    }

    /// <summary>Stores the finalization, replacing a Pending one; throws ExchangeRunConflictException when the stored one is already Finalized.</summary>
    public ValueTask SaveCheckpointFinalizationAsync(
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_checkpointFinalizations.TryGetValue(finalization.CommitRunId, out var current)
                && current.Status == CheckpointFinalizationStatus.Finalized)
            {
                throw new ExchangeRunConflictException(
                    $"Checkpoint finalization for '{finalization.CommitRunId.Value}' is already final.");
            }
            _checkpointFinalizations[finalization.CommitRunId] = finalization with { };
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns a copy of the finalization, or null when absent.</summary>
    public ValueTask<CommitCheckpointFinalization?> GetCheckpointFinalizationAsync(
        CommitRunId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_checkpointFinalizations.TryGetValue(id, out var finalization)
                ? finalization with { }
                : null);
        }
    }

    private static DryRunArtifact Snapshot(DryRunArtifact artifact) => artifact with
    {
        Proposal = artifact.Proposal with { },
        NormalizedEffects = artifact.NormalizedEffects
            .Select(effect => effect with
            {
                Metadata = effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            })
            .ToImmutableArray(),
        Evaluations = artifact.Evaluations.Select(evaluation => evaluation with
        {
            Effect = evaluation.Effect with
            {
                Metadata = evaluation.Effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            },
            Outcome = evaluation.Outcome with { },
        }).ToImmutableArray(),
        Census = artifact.Census with { },
    };

    private static CommitRunArtifact Snapshot(CommitRunArtifact artifact) => artifact with
    {
        Effects = artifact.Effects
            .Select(result => result with
            {
                Effect = result.Effect with
                {
                    Metadata = result.Effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
                },
                Outcome = result.Outcome with { },
            })
            .ToImmutableArray(),
        Census = artifact.Census with { },
    };
}

/// <summary>Creates immutable review evidence without writing target records.</summary>
public sealed class DataExchangeRuntime(IExchangeRunStore runs, TimeProvider clock, IRunLifecyclePolicyPort lifecycle)
{
    /// <summary>Store the dry runs are saved to.</summary>
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    /// <summary>Time source for request timestamps and retention checks.</summary>
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Derives retention and legal hold for new runs.</summary>
    private readonly IRunLifecyclePolicyPort _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    /// <summary>Returns true only when no legal hold applies and both the run's and the current policy's retain-until dates have passed; throws run.not_found for an unknown dry run.</summary>
    public async ValueTask<bool> CanDisposeDryRunAsync(DryRunId id, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetDryRunAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The dry run does not exist.");
        return await CanDisposeAsync(run.TenantId, run.RetentionClass, run.RequestedAt,
            run.RetainUntil, run.LegalHold, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns true only when no legal hold applies and both the run's and the current policy's retain-until dates have passed; throws run.not_found for an unknown commit run.</summary>
    public async ValueTask<bool> CanDisposeCommitRunAsync(CommitRunId id, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetCommitRunAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The commit run does not exist.");
        return await CanDisposeAsync(run.BatchDerivation.TenantId, run.RetentionClass, run.RequestedAt,
            run.RetainUntil, run.LegalHold, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> CanDisposeAsync(string tenantId, string retentionClass,
        DateTimeOffset requestedAt, DateTimeOffset retainUntil, bool legalHold, CancellationToken cancellationToken)
    {
        var current = await _lifecycle.DeriveAsync(tenantId, retentionClass, requestedAt, cancellationToken).ConfigureAwait(false);
        return !legalHold && !current.LegalHold && _clock.GetUtcNow() >= retainUntil && _clock.GetUtcNow() >= current.RetainUntil;
    }

    /// <summary>Persists a dry run with retention from the lifecycle policy and never writes target records; a superseded dry run must exist in the same tenant and differ from the new id (run.not_found, run.stale).</summary>
    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        DryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SupersedesDryRunId is { } predecessorId)
        {
            var predecessor = await _runs.GetDryRunAsync(predecessorId, cancellationToken).ConfigureAwait(false)
                ?? throw new DataExchangeCommitRefusedException("run.not_found", "The superseded dry run does not exist.");
            if (!StringComparer.Ordinal.Equals(predecessor.TenantId, request.TenantId)
                || request.PrescribedId == predecessorId)
            {
                throw new DataExchangeCommitRefusedException("run.stale", "Supersession must reference a different dry run in the same tenant.");
            }
        }
        var requestedAt = _clock.GetUtcNow();
        var retention = await _lifecycle.DeriveAsync(request.TenantId, request.RetentionClass, requestedAt, cancellationToken).ConfigureAwait(false);
        var evaluations = Evaluations(request);
        var artifact = new DryRunArtifact(
            request.PrescribedId ?? DryRunId.New(),
            request.TenantId,
            request.RequestedBy,
            requestedAt,
            request.Proposal with { },
            evaluations.Select(evaluation => evaluation.Effect with
            {
                Metadata = evaluation.Effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            }).ToImmutableArray(),
            request.CandidateCheckpoint,
            request.SnapshotReference,
            request.AuthorizationContextReference,
            request.RetentionClass,
            retention.RetainUntil,
            evaluations,
            Census(evaluations),
            request.ExpectedCheckpoint,
            retention.LegalHold,
            request.SupersedesDryRunId);
        await _runs.SaveDryRunAsync(artifact, cancellationToken).ConfigureAwait(false);
        return artifact;
    }

    private static ImmutableArray<DryRunEffectEvaluation> Evaluations(DryRunRequest request)
        => request.Evaluations?.Select(evaluation => evaluation with
        {
            Effect = evaluation.Effect with
            {
                Metadata = evaluation.Effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            },
            Outcome = evaluation.Outcome with { },
        }).ToImmutableArray()
        ?? request.Effects.Select(effect => new DryRunEffectEvaluation(
            effect,
            new EffectTerminalOutcome(ExchangeEffectStatus.Applied, "mapping.proposed"))).ToImmutableArray();

    private static ExchangeCensus Census(IReadOnlyList<DryRunEffectEvaluation> evaluations)
    {
        var statuses = evaluations.Select(evaluation => evaluation.Outcome.Status).ToArray();
        return new(
            statuses.Count(status => status == ExchangeEffectStatus.Applied),
            statuses.Count(status => status == ExchangeEffectStatus.Skipped),
            statuses.Count(status => status == ExchangeEffectStatus.Conflicted),
            statuses.Count(status => status == ExchangeEffectStatus.Rejected),
            statuses.Count(status => status == ExchangeEffectStatus.Failed),
            statuses.Count(status => status == ExchangeEffectStatus.Halted));
    }
}
