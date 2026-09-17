using System.Collections.Immutable;
using System.Text.Json.Serialization;

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
    IReadOnlyDictionary<string, string> Metadata);

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
    bool LegalHold = false,
    DryRunId? SupersedesDryRunId = null);

public sealed class ExchangeRunConflictException(string message) : Exception(message);

public interface IExchangeRunStore
{
    ValueTask SaveDryRunAsync(DryRunArtifact artifact, CancellationToken cancellationToken = default);
    ValueTask<DryRunArtifact?> GetDryRunAsync(DryRunId id, CancellationToken cancellationToken = default);
    ValueTask SaveCommitRunAsync(CommitRunArtifact artifact, CancellationToken cancellationToken = default);
    ValueTask<CommitRunArtifact?> GetCommitRunAsync(CommitRunId id, CancellationToken cancellationToken = default);
}

public sealed class InMemoryExchangeRunStore : IExchangeRunStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DryRunId, DryRunArtifact> _dryRuns = [];
    private readonly Dictionary<CommitRunId, CommitRunArtifact> _commitRuns = [];

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

    private static DryRunArtifact Snapshot(DryRunArtifact artifact) => artifact with
    {
        Proposal = artifact.Proposal with { },
        NormalizedEffects = artifact.NormalizedEffects
            .Select(effect => effect with
            {
                Metadata = effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            })
            .ToImmutableArray(),
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
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly IRunLifecyclePolicyPort _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public async ValueTask<bool> CanDisposeDryRunAsync(DryRunId id, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetDryRunAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The dry run does not exist.");
        return await CanDisposeAsync(run.TenantId, run.RetentionClass, run.RequestedAt,
            run.RetainUntil, run.LegalHold, cancellationToken).ConfigureAwait(false);
    }

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

    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        DryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestedAt = _clock.GetUtcNow();
        var retention = await _lifecycle.DeriveAsync(request.TenantId, request.RetentionClass, requestedAt, cancellationToken).ConfigureAwait(false);
        var artifact = new DryRunArtifact(
            new DryRunId(Guid.NewGuid().ToString("N")),
            request.TenantId,
            request.RequestedBy,
            requestedAt,
            request.Proposal with { },
            request.Effects.Select(effect => effect with
            {
                Metadata = effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            }).ToImmutableArray(),
            request.CandidateCheckpoint,
            request.SnapshotReference,
            request.AuthorizationContextReference,
            request.RetentionClass,
            retention.RetainUntil,
            retention.LegalHold,
            SupersedesDryRunId: request.SupersedesDryRunId);
        await _runs.SaveDryRunAsync(artifact, cancellationToken).ConfigureAwait(false);
        return artifact;
    }
}
