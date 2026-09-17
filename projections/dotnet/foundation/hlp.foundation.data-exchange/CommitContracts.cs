using System.Collections.Concurrent;
using System.Text.Json;

namespace Harborline.Foundation.DataExchange;

public enum ExchangeEffectStatus
{
    Applied,
    Skipped,
    Conflicted,
    Rejected,
    Failed,
    Halted,
}

public sealed record EffectTerminalOutcome(ExchangeEffectStatus Status, string Code);

public sealed record ExchangeOutcomePolicy(
    ExchangeEffectStatus Status,
    bool Retryable,
    bool CorrectionRequired,
    bool AcknowledgementSafe,
    string OrdinaryReaderCode);

public static class ExchangeOutcomePolicies
{
    private static readonly IReadOnlyDictionary<ExchangeEffectStatus, ExchangeOutcomePolicy> Policies =
        new Dictionary<ExchangeEffectStatus, ExchangeOutcomePolicy>
        {
            [ExchangeEffectStatus.Applied] = new(ExchangeEffectStatus.Applied, false, false, true, "applied"),
            [ExchangeEffectStatus.Skipped] = new(ExchangeEffectStatus.Skipped, false, false, true, "skipped"),
            [ExchangeEffectStatus.Conflicted] = new(ExchangeEffectStatus.Conflicted, false, true, true, "conflicted"),
            [ExchangeEffectStatus.Rejected] = new(ExchangeEffectStatus.Rejected, false, true, true, "rejected"),
            [ExchangeEffectStatus.Failed] = new(ExchangeEffectStatus.Failed, true, false, false, "failed"),
            [ExchangeEffectStatus.Halted] = new(ExchangeEffectStatus.Halted, true, false, false, "halted"),
        };

    public static ExchangeOutcomePolicy For(ExchangeEffectStatus status) => Policies[status];
}

public sealed record EffectLedgerEntry(
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    EffectTerminalOutcome Outcome,
    DateTimeOffset RecordedAt);

public enum ExchangeRunTerminalStatus
{
    Completed,
    CompletedWithRefusals,
    Halted,
}

public sealed record ExchangeCensus(
    int Applied,
    int Skipped,
    int Conflicted,
    int Rejected,
    int Failed,
    int Halted)
{
    public int Accounted => Applied + Skipped + Conflicted + Rejected + Failed + Halted;
}

public sealed record CommitEffectResult(
    ProposedEffect Effect,
    EffectIdempotencyIdentity EffectIdentity,
    EffectTerminalOutcome Outcome,
    bool ReplayedFromLedger);

public sealed record CommitRunArtifact(
    CommitRunId Id,
    DryRunId ApprovedDryRunId,
    BatchIdentity BatchIdentity,
    BatchIdentityInputs BatchDerivation,
    DateTimeOffset RequestedAt,
    IReadOnlyList<CommitEffectResult> Effects,
    ExchangeCensus Census,
    ExchangeRunTerminalStatus TerminalStatus,
    string? DurableCheckpoint,
    string RetentionClass,
    DateTimeOffset RetainUntil,
    bool LegalHold);

public sealed record AcknowledgementPolicy
{
    public AcknowledgementPolicy(IReadOnlySet<ExchangeEffectStatus> safeOutcomes)
    {
        ArgumentNullException.ThrowIfNull(safeOutcomes);
        var invalid = safeOutcomes.Where(status => ExchangeOutcomePolicies.For(status).Retryable).ToArray();
        if (invalid.Length > 0)
        {
            throw new ArgumentException("Retryable Failed or Halted outcomes cannot advance a checkpoint.", nameof(safeOutcomes));
        }
        SafeOutcomes = safeOutcomes.ToHashSet();
    }

    public IReadOnlySet<ExchangeEffectStatus> SafeOutcomes { get; }
}

public sealed record CommitBounds
{
    public CommitBounds(int maxWindowSize, int maxConcurrency, int maxCommandBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWindowSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommandBytes);
        MaxWindowSize = maxWindowSize;
        MaxConcurrency = maxConcurrency;
        MaxCommandBytes = maxCommandBytes;
    }

    public int MaxWindowSize { get; }

    public int MaxConcurrency { get; }

    public int MaxCommandBytes { get; }
}

public sealed record CanonicalRecordsCommand(
    CommitRunId CommitRunId,
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    string TargetContract,
    ProposedEffect Effect,
    CanonicalEffectPayload Payload);

public sealed class DataExchangeCommitRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IExchangeCommitAuthority
{
    ValueTask<bool> CanCommitAsync(
        DryRunArtifact dryRun,
        CancellationToken cancellationToken = default);
}

public interface ITargetAccessGate
{
    ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default);
}

public interface ICanonicalRecordsCommandPort
{
    ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default);
}

public interface IEffectOutcomeStore
{
    ValueTask<EffectClaim> ClaimAsync(
        BatchIdentity batchIdentity,
        EffectIdempotencyIdentity effectIdentity,
        AttemptId attemptId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(EffectLedgerEntry entry, CancellationToken cancellationToken = default);
}

public sealed record EffectClaim(bool Acquired, EffectLedgerEntry? ExistingOutcome);

public interface IAcquisitionCheckpointStore
{
    ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);

    ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string? expectedCurrentCheckpoint,
        string checkpoint,
        BatchIdentity batchIdentity,
        int sourceOrdinal,
        CancellationToken cancellationToken = default);
}

public sealed class CheckpointConflictException(string message) : Exception(message)
{
    public string Code => "checkpoint.conflict";
}

public sealed class InMemoryEffectOutcomeStore : IEffectOutcomeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<EffectIdempotencyIdentity, EffectLedgerEntry> _entries = [];
    private readonly Dictionary<EffectIdempotencyIdentity, (AttemptId AttemptId, DateTimeOffset LeaseUntil)> _claims = [];

    public ValueTask<EffectClaim> ClaimAsync(
        BatchIdentity batchIdentity,
        EffectIdempotencyIdentity effectIdentity,
        AttemptId attemptId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var existing = _entries.GetValueOrDefault(effectIdentity);
            if (existing is not null && !ExchangeOutcomePolicies.For(existing.Outcome.Status).Retryable)
            {
                return ValueTask.FromResult(new EffectClaim(false, existing));
            }
            if (_claims.TryGetValue(effectIdentity, out var currentClaim)
                && currentClaim.LeaseUntil > claimedAt)
            {
                return ValueTask.FromResult(new EffectClaim(false, null));
            }
            _claims[effectIdentity] = (attemptId, leaseUntil);
            return ValueTask.FromResult(new EffectClaim(true, null));
        }
    }

    public ValueTask CompleteAsync(EffectLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_claims.TryGetValue(entry.EffectIdentity, out var claim)
                || claim.AttemptId != entry.AttemptId)
            {
                throw new ExchangeRunConflictException("The effect attempt no longer owns the active claim.");
            }
            if (_entries.TryGetValue(entry.EffectIdentity, out var existing)
                && !ExchangeOutcomePolicies.For(existing.Outcome.Status).Retryable)
            {
                throw new ExchangeRunConflictException("A resolved effect outcome cannot be overwritten.");
            }
            _entries[entry.EffectIdentity] = entry;
            _claims.Remove(entry.EffectIdentity);
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class InMemoryAcquisitionCheckpointStore : IAcquisitionCheckpointStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string TenantId, string DefinitionId), CheckpointState> _checkpoints = [];

    public ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_checkpoints.GetValueOrDefault((tenantId, definitionId))?.Value);
        }
    }

    public ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string? expectedCurrentCheckpoint,
        string checkpoint,
        BatchIdentity batchIdentity,
        int sourceOrdinal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = _checkpoints.GetValueOrDefault((tenantId, definitionId));
            var isSameBatchAdvance = current is not null
                && current.BatchIdentity == batchIdentity
                && (sourceOrdinal > current.SourceOrdinal
                    || sourceOrdinal == current.SourceOrdinal
                        && StringComparer.Ordinal.Equals(checkpoint, current.Value));
            if (!StringComparer.Ordinal.Equals(current?.Value, expectedCurrentCheckpoint)
                && !isSameBatchAdvance)
            {
                throw new CheckpointConflictException(
                    $"Checkpoint changed from expected '{expectedCurrentCheckpoint ?? "<none>"}' to '{current?.Value ?? "<none>"}'.");
            }
            _checkpoints[(tenantId, definitionId)] = new(checkpoint, batchIdentity, sourceOrdinal);
        }
        return ValueTask.CompletedTask;
    }

    private sealed record CheckpointState(string Value, BatchIdentity BatchIdentity, int SourceOrdinal);
}

/// <summary>Promotes one reviewed proposal through bounded canonical Records commands.</summary>
public sealed class DataExchangeCommitter(
    IExchangeRunStore runs,
    IExchangeCommitAuthority authority,
    ITargetAccessGate access,
    ICanonicalRecordsCommandPort commands,
    IEffectOutcomeStore outcomes,
    IAcquisitionCheckpointStore checkpoints,
    IProtectedEffectPayloadStore payloads,
    TimeProvider clock,
    CommitBounds bounds)
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IExchangeCommitAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly ITargetAccessGate _access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly ICanonicalRecordsCommandPort _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    private readonly IEffectOutcomeStore _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
    private readonly IAcquisitionCheckpointStore _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly IProtectedEffectPayloadStore _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly CommitBounds _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

    public DataExchangeCommitter(
        IExchangeRunStore runs,
        IExchangeCommitAuthority authority,
        ITargetAccessGate access,
        ICanonicalRecordsCommandPort commands,
        IEffectOutcomeStore outcomes,
        IAcquisitionCheckpointStore checkpoints,
        TimeProvider clock,
        CommitBounds bounds)
        : this(
            runs,
            authority,
            access,
            commands,
            outcomes,
            checkpoints,
            new InMemoryProtectedEffectPayloadStore(),
            clock,
            bounds)
    {
    }

    public async ValueTask<CommitRunArtifact> CommitAsync(
        DryRunId dryRunId,
        ProposalFingerprint currentProposal,
        AcknowledgementPolicy? acknowledgementPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var dryRun = await _runs.GetDryRunAsync(dryRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The dry run does not exist.");
        if (dryRun.Proposal != currentProposal)
        {
            throw new DataExchangeCommitRefusedException(
                "run.stale",
                "Proposal-affecting inputs changed; create a new dry run.");
        }
        if (!await _authority.CanCommitAsync(dryRun, cancellationToken).ConfigureAwait(false))
        {
            throw new DataExchangeCommitRefusedException(
                "commit.forbidden",
                "Data Exchange commit authority was refused.");
        }
        if (dryRun.NormalizedEffects.Count > _bounds.MaxWindowSize)
        {
            throw new DataExchangeCommitRefusedException(
                "commit.window_exceeded",
                "The reviewed effect window exceeds the bounded commit size.");
        }

        var policy = acknowledgementPolicy ?? new AcknowledgementPolicy(
            Enum.GetValues<ExchangeEffectStatus>()
                .Where(status => ExchangeOutcomePolicies.For(status).AcknowledgementSafe)
                .ToHashSet());
        var commitRunId = new CommitRunId(Guid.NewGuid().ToString("N"));
        var batchDerivation = new BatchIdentityInputs(
            dryRun.TenantId,
            dryRun.Proposal.DefinitionId,
            dryRun.Proposal.DefinitionVersion,
            dryRun.Proposal.MappingId,
            dryRun.Proposal.MappingVersion,
            dryRun.Proposal.MappingDigest,
            dryRun.Proposal.ConnectorId,
            dryRun.Proposal.ConnectorVersion,
            dryRun.Proposal.SourceFingerprint,
            dryRun.Proposal.InputBoundary,
            dryRun.Proposal.TargetContract);
        var batch = ExchangeIdentity.DeriveBatch(batchDerivation);
        var prepared = await PrepareWindowAsync(dryRun, commitRunId, batch, cancellationToken).ConfigureAwait(false);
        var completed = new ConcurrentQueue<CommitEffectResult>();
        await Parallel.ForEachAsync(
            prepared,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _bounds.MaxConcurrency,
            },
            async (effect, token) => completed.Enqueue(
                await ApplyEffectAsync(batch, effect, token).ConfigureAwait(false)))
            .ConfigureAwait(false);
        var results = completed.OrderBy(result => result.Effect.SourceOrdinal).ToArray();

        string? durableCheckpoint = null;
        var durableCheckpointOrdinal = -1;
        foreach (var result in results.OrderBy(item => item.Effect.SourceOrdinal))
        {
            if (!policy.SafeOutcomes.Contains(result.Outcome.Status))
            {
                break;
            }
            durableCheckpoint = result.Effect.BoundaryAfter;
            durableCheckpointOrdinal = result.Effect.SourceOrdinal;
        }
        var census = Census(results);
        var terminalStatus = census.Halted > 0
            ? ExchangeRunTerminalStatus.Halted
            : census.Skipped + census.Conflicted + census.Rejected + census.Failed > 0
                ? ExchangeRunTerminalStatus.CompletedWithRefusals
                : ExchangeRunTerminalStatus.Completed;
        var commitRun = new CommitRunArtifact(
            commitRunId,
            dryRun.Id,
            batch,
            batchDerivation,
            _clock.GetUtcNow(),
            results,
            census,
            terminalStatus,
            durableCheckpoint,
            dryRun.RetentionClass,
            dryRun.RetainUntil,
            dryRun.LegalHold);
        var finalization = new CommitCheckpointFinalization(
            commitRun.Id,
            dryRun.ExpectedCheckpoint,
            durableCheckpoint,
            durableCheckpoint is null ? CheckpointFinalizationStatus.Finalized : CheckpointFinalizationStatus.Pending,
            _clock.GetUtcNow());
        await _runs.SaveCommitRunWithCheckpointFinalizationAsync(
            commitRun,
            finalization,
            CancellationToken.None).ConfigureAwait(false);
        if (durableCheckpoint is not null)
        {
            await _checkpoints.PromoteAsync(
                dryRun.TenantId,
                dryRun.Proposal.DefinitionId,
                dryRun.ExpectedCheckpoint,
                durableCheckpoint,
                batch,
                durableCheckpointOrdinal,
                CancellationToken.None).ConfigureAwait(false);
            await _runs.SaveCheckpointFinalizationAsync(
                finalization with
                {
                    Status = CheckpointFinalizationStatus.Finalized,
                    RecordedAt = _clock.GetUtcNow(),
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        return commitRun;
    }

    private async ValueTask<IReadOnlyList<PreparedCommitEffect>> PrepareWindowAsync(
        DryRunArtifact dryRun,
        CommitRunId commitRunId,
        BatchIdentity batch,
        CancellationToken cancellationToken)
    {
        var prepared = new List<PreparedCommitEffect>(dryRun.Evaluations.Count);
        foreach (var evaluation in dryRun.Evaluations.OrderBy(item => item.Effect.SourceOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effect = evaluation.Effect;
            var effectIdentity = ExchangeIdentity.DeriveEffect(
                batch,
                dryRun.Proposal.TargetContract,
                effect.SourceRecordIdentity,
                effect.SourceRecordVersion,
                effect.EffectDiscriminator);
            var attempt = AttemptId.New();
            var precomputedOutcome = evaluation.Outcome.Code == "mapping.proposed"
                ? null
                : evaluation.Outcome;
            if (precomputedOutcome is not null)
            {
                prepared.Add(new(effect, effectIdentity, attempt, null, precomputedOutcome));
                continue;
            }
            var payload = effect.PayloadReference is null
                ? new CanonicalEffectPayload(dryRun.Proposal.TargetContract, new Dictionary<string, object?>())
                : await _payloads.GetAsync(effect.PayloadReference, cancellationToken).ConfigureAwait(false)
                    ?? throw new DataExchangeCommitRefusedException(
                        "commit.payload_missing",
                        "The protected canonical effect payload is unavailable.");
            var command = new CanonicalRecordsCommand(
                commitRunId,
                batch,
                effectIdentity,
                attempt,
                dryRun.Proposal.TargetContract,
                effect,
                payload);
            if (JsonSerializer.SerializeToUtf8Bytes(command).Length > _bounds.MaxCommandBytes)
            {
                throw new DataExchangeCommitRefusedException(
                    "commit.command_payload_exceeded",
                    "A canonical Records command exceeds the bounded payload size.");
            }
            prepared.Add(new(effect, effectIdentity, attempt, command, null));
        }
        return prepared;
    }

    private async ValueTask<CommitEffectResult> ApplyEffectAsync(
        BatchIdentity batch,
        PreparedCommitEffect prepared,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var claim = await _outcomes.ClaimAsync(
            batch,
            prepared.EffectIdentity,
            prepared.AttemptId,
            now,
            now.AddMinutes(5),
            cancellationToken).ConfigureAwait(false);
        if (!claim.Acquired && claim.ExistingOutcome is not null)
        {
            return new(prepared.Effect, prepared.EffectIdentity, claim.ExistingOutcome.Outcome, true);
        }
        if (!claim.Acquired)
        {
            return new(
                prepared.Effect,
                prepared.EffectIdentity,
                new EffectTerminalOutcome(ExchangeEffectStatus.Halted, "commit.effect_in_progress"),
                false);
        }

        EffectTerminalOutcome outcome;
        if (prepared.PrecomputedOutcome is not null)
        {
            outcome = prepared.PrecomputedOutcome;
        }
        else if (!await _access.CanApplyAsync(prepared.Effect, cancellationToken).ConfigureAwait(false))
        {
            outcome = new(ExchangeEffectStatus.Rejected, "target.access_refused");
        }
        else
        {
            try
            {
                outcome = await _commands.ApplyAsync(prepared.Command!, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                outcome = new(ExchangeEffectStatus.Failed, "records.command_failed");
            }
        }
        await _outcomes.CompleteAsync(
            new(batch, prepared.EffectIdentity, prepared.AttemptId, outcome, _clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        return new(prepared.Effect, prepared.EffectIdentity, outcome, false);
    }

    private sealed record PreparedCommitEffect(
        ProposedEffect Effect,
        EffectIdempotencyIdentity EffectIdentity,
        AttemptId AttemptId,
        CanonicalRecordsCommand? Command,
        EffectTerminalOutcome? PrecomputedOutcome);

    private static ExchangeCensus Census(IEnumerable<CommitEffectResult> effects)
    {
        var statuses = effects.Select(effect => effect.Outcome.Status).ToArray();
        return new(
            statuses.Count(status => status == ExchangeEffectStatus.Applied),
            statuses.Count(status => status == ExchangeEffectStatus.Skipped),
            statuses.Count(status => status == ExchangeEffectStatus.Conflicted),
            statuses.Count(status => status == ExchangeEffectStatus.Rejected),
            statuses.Count(status => status == ExchangeEffectStatus.Failed),
            statuses.Count(status => status == ExchangeEffectStatus.Halted));
    }
}
