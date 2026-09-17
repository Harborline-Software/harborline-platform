using System.Collections.Immutable;

namespace Harborline.Foundation.DataExchange;

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
    string DependencyFingerprint);

/// <summary>Normalized non-sensitive evidence for one intended canonical target effect.</summary>
public sealed record ProposedEffect(
    int SourceOrdinal,
    string SourceRecordIdentity,
    string SourceRecordVersion,
    string EffectDiscriminator,
    string BoundaryAfter,
    IReadOnlyDictionary<string, string> Metadata,
    string? PayloadReference = null);

public sealed record DryRunEffectEvaluation(
    ProposedEffect Effect,
    EffectTerminalOutcome Outcome);

public sealed record DryRunRequest(
    string TenantId,
    string RequestedBy,
    ProposalFingerprint Proposal,
    IReadOnlyList<ProposedEffect> Effects,
    string CandidateCheckpoint,
    string? SnapshotReference,
    string AuthorizationContextReference,
    string RetentionClass,
    DateTimeOffset RetainUntil,
    IReadOnlyList<DryRunEffectEvaluation>? Evaluations = null,
    DryRunId? PrescribedId = null,
    string? ExpectedCheckpoint = null);

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
    bool LegalHold = false);

public enum CheckpointFinalizationStatus
{
    Pending,
    Finalized,
}

public sealed record CommitCheckpointFinalization(
    CommitRunId CommitRunId,
    string? ExpectedCheckpoint,
    string? PromotedCheckpoint,
    CheckpointFinalizationStatus Status,
    DateTimeOffset RecordedAt);

public sealed class ExchangeRunConflictException(string message) : Exception(message);

public interface IExchangeRunStore
{
    ValueTask SaveDryRunAsync(DryRunArtifact artifact, CancellationToken cancellationToken = default);
    ValueTask<DryRunArtifact?> GetDryRunAsync(DryRunId id, CancellationToken cancellationToken = default);
    ValueTask SaveCommitRunAsync(CommitRunArtifact artifact, CancellationToken cancellationToken = default);
    ValueTask SaveCommitRunWithCheckpointFinalizationAsync(
        CommitRunArtifact artifact,
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default);
    ValueTask<CommitRunArtifact?> GetCommitRunAsync(CommitRunId id, CancellationToken cancellationToken = default);
    ValueTask SaveCheckpointFinalizationAsync(CommitCheckpointFinalization finalization, CancellationToken cancellationToken = default);
    ValueTask<CommitCheckpointFinalization?> GetCheckpointFinalizationAsync(CommitRunId id, CancellationToken cancellationToken = default);
}

public sealed class InMemoryExchangeRunStore : IExchangeRunStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DryRunId, DryRunArtifact> _dryRuns = [];
    private readonly Dictionary<CommitRunId, CommitRunArtifact> _commitRuns = [];
    private readonly Dictionary<CommitRunId, CommitCheckpointFinalization> _checkpointFinalizations = [];

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

    public ValueTask SaveCommitRunAsync(
        CommitRunArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_commitRuns.TryAdd(artifact.Id, Snapshot(artifact)))
            {
                throw new ExchangeRunConflictException($"Commit run '{artifact.Id.Value}' already exists.");
            }
        }
        return ValueTask.CompletedTask;
    }

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
public sealed class DataExchangeRuntime(IExchangeRunStore runs, TimeProvider clock)
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        DryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evaluations = Evaluations(request);
        var artifact = new DryRunArtifact(
            request.PrescribedId ?? DryRunId.New(),
            request.TenantId,
            request.RequestedBy,
            _clock.GetUtcNow(),
            request.Proposal with { },
            evaluations.Select(evaluation => evaluation.Effect with
            {
                Metadata = evaluation.Effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            }).ToImmutableArray(),
            request.CandidateCheckpoint,
            request.SnapshotReference,
            request.AuthorizationContextReference,
            request.RetentionClass,
            request.RetainUntil,
            evaluations,
            Census(evaluations),
            request.ExpectedCheckpoint);
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
