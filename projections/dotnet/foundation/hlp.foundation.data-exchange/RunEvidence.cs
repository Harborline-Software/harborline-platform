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
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DryRunRequest(
    string TenantId,
    string RequestedBy,
    ProposalFingerprint Proposal,
    IReadOnlyList<ProposedEffect> Effects,
    string CandidateCheckpoint,
    string? SnapshotReference,
    string AuthorizationContextReference,
    string RetentionClass,
    DateTimeOffset RetainUntil);

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
    bool LegalHold = false);

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
public sealed class DataExchangeRuntime(IExchangeRunStore runs, TimeProvider clock)
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async ValueTask<DryRunArtifact> CreateDryRunAsync(
        DryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var artifact = new DryRunArtifact(
            new DryRunId(Guid.NewGuid().ToString("N")),
            request.TenantId,
            request.RequestedBy,
            _clock.GetUtcNow(),
            request.Proposal with { },
            request.Effects.Select(effect => effect with
            {
                Metadata = effect.Metadata.ToImmutableDictionary(StringComparer.Ordinal),
            }).ToImmutableArray(),
            request.CandidateCheckpoint,
            request.SnapshotReference,
            request.AuthorizationContextReference,
            request.RetentionClass,
            request.RetainUntil);
        await _runs.SaveDryRunAsync(artifact, cancellationToken).ConfigureAwait(false);
        return artifact;
    }
}
