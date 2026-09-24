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
    public AcknowledgementPolicy(
        string id,
        IReadOnlySet<ExchangeEffectStatus> safeOutcomes,
        IReadOnlySet<ExchangeEffectStatus> retryableOutcomes,
        bool correctConflicts = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(safeOutcomes);
        ArgumentNullException.ThrowIfNull(retryableOutcomes);
        if (safeOutcomes.Any(status => ExchangeOutcomePolicies.For(status).Retryable))
        {
            throw new ArgumentException("Retryable Failed or Halted outcomes cannot advance a checkpoint.", nameof(safeOutcomes));
        }

        Id = id;
        SafeOutcomes = safeOutcomes.ToHashSet();
        RetryableOutcomes = retryableOutcomes.ToHashSet();
        CorrectConflicts = correctConflicts;
    }

    public string Id { get; init; }
    public IReadOnlySet<ExchangeEffectStatus> SafeOutcomes { get; init; }
    public IReadOnlySet<ExchangeEffectStatus> RetryableOutcomes { get; init; }
    public bool CorrectConflicts { get; init; }
}

public sealed record CommitOptions(bool AllOrNothingRollback = false, bool MultiCommandAtomicCommit = false);

/// <summary>Resolves source-capability-owned outcome metadata; unknown policies return null.</summary>
public interface ISourceOutcomePolicyPort
{
    ValueTask<AcknowledgementPolicy?> ResolveAsync(ProposalFingerprint proposal, CancellationToken cancellationToken = default);
}

/// <summary>Reevaluates live semantic dependencies and effects through trusted host adapters.</summary>
public interface IProposalEvaluationPort
{
    ValueTask<DryRunRequest> EvaluateAsync(DryRunArtifact approved, CancellationToken cancellationToken = default);
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

public sealed record TargetAuthorizationContext(string TenantId, string ActorId, string ContextReference);

public sealed record CanonicalRecordsCommand(
    CommitRunId CommitRunId,
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    string TargetContract,
    ProposedEffect Effect,
    TargetAuthorizationContext Authorization,
    CanonicalEffectPayload Payload);

public sealed class DataExchangeCommitRefusedException(string code, string message, DryRunId? supersedingDryRunId = null) : Exception(message)
{
    public string Code { get; } = code;
    public DryRunId? SupersedingDryRunId { get; } = supersedingDryRunId;
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

/// <summary>A domain-owned forward correction of a conflicted canonical command, never batch undo.</summary>
public sealed record CanonicalForwardCorrectionCommand(CanonicalRecordsCommand OriginalCommand, EffectTerminalOutcome Conflict);

public interface ICanonicalForwardCorrectionPort
{
    ValueTask<EffectTerminalOutcome> CorrectAndRecordAsync(CanonicalForwardCorrectionCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// The only target execution boundary. Each command (including forward correction) independently
/// performs target authorization, validation, idempotency, audit and its own transaction.
/// ExecuteAndRecordAsync and CorrectAndRecordAsync atomically commit the target effect and durable
/// outcome before returning. A lost response must remain discoverable by GetOutcomeAsync; retries
/// must preserve successful effects. There is no separate caller-owned outcome write.
/// </summary>
public interface ICanonicalTargetCommandPort : ICanonicalForwardCorrectionPort
{
    ValueTask<EffectClaim> ClaimAsync(
        BatchIdentity batchIdentity,
        EffectIdempotencyIdentity effectIdentity,
        AttemptId attemptId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    ValueTask<EffectTerminalOutcome> ExecuteAndRecordAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<EffectLedgerEntry?> GetOutcomeAsync(
        EffectIdempotencyIdentity identity,
        CancellationToken cancellationToken = default);
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
    ICanonicalTargetCommandPort commands,
    IAcquisitionCheckpointStore checkpoints,
    IProtectedEffectPayloadStore payloads,
    TimeProvider clock,
    CommitBounds bounds,
    IProposalEvaluationPort proposals,
    ISourceOutcomePolicyPort sourcePolicies,
    IRunLifecyclePolicyPort lifecycle,
    ICanonicalTargetRegistryPort targets)
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IExchangeCommitAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly ITargetAccessGate _access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly ICanonicalTargetCommandPort _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    private readonly IAcquisitionCheckpointStore _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly IProtectedEffectPayloadStore _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly CommitBounds _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

    private readonly IProposalEvaluationPort _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly ISourceOutcomePolicyPort _sourcePolicies = sourcePolicies ?? throw new ArgumentNullException(nameof(sourcePolicies));

    private readonly IRunLifecyclePolicyPort _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    private readonly ICanonicalTargetRegistryPort _targets = targets ?? throw new ArgumentNullException(nameof(targets));

    public DataExchangeCommitter(
        IExchangeRunStore runs,
        IExchangeCommitAuthority authority,
        ITargetAccessGate access,
        ICanonicalTargetCommandPort commands,
        IAcquisitionCheckpointStore checkpoints,
        TimeProvider clock,
        CommitBounds bounds,
        IProposalEvaluationPort proposals,
        ISourceOutcomePolicyPort sourcePolicies,
        IRunLifecyclePolicyPort lifecycle,
        ICanonicalTargetRegistryPort targets)
        : this(
            runs,
            authority,
            access,
            commands,
            checkpoints,
            new InMemoryProtectedEffectPayloadStore(),
            clock,
            bounds,
            proposals,
            sourcePolicies,
            lifecycle,
            targets)
    {
    }

    public async ValueTask<CommitRunArtifact> CommitAsync(
        DryRunId dryRunId,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options is { AllOrNothingRollback: true } or { MultiCommandAtomicCommit: true })
            throw new DataExchangeCommitRefusedException("commit.rollback_refused", "Only per-command commit and domain-owned forward correction are supported.");
        var dryRun = await _runs.GetDryRunAsync(dryRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new DataExchangeCommitRefusedException("run.not_found", "The dry run does not exist.");
        var current = await _proposals.EvaluateAsync(dryRun, cancellationToken).ConfigureAwait(false);
        var batchDerivation = BatchIdentityInputs.From(dryRun.TenantId, dryRun.Proposal);
        var reevaluatedEffects = current.Evaluations?.Select(evaluation => evaluation.Effect).ToArray()
            ?? current.Effects.ToArray();
        if (batchDerivation != BatchIdentityInputs.From(current.TenantId, current.Proposal)
            || !EffectsEqual(dryRun.NormalizedEffects, reevaluatedEffects))
        {
            var superseding = await new DataExchangeRuntime(_runs, _clock, _lifecycle).CreateDryRunAsync(
                current with { SupersedesDryRunId = dryRun.Id }, cancellationToken).ConfigureAwait(false);
            throw new DataExchangeCommitRefusedException("run.stale",
                "Proposal-affecting inputs changed; review the superseding dry run.", superseding.Id);
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

        var target = await _targets.ResolveAsync(dryRun.Proposal.TargetContract, cancellationToken).ConfigureAwait(false);
        if (target is null || target.Contract != dryRun.Proposal.TargetContract
            || !target.Contract.StartsWith("records.", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(target.Version)
            || !target.Contract.EndsWith("/" + target.Version, StringComparison.Ordinal))
        {
            throw new DataExchangeCommitRefusedException("target.contract_unregistered", "The target must resolve to a versioned canonical Records contract.");
        }

        var policy = await _sourcePolicies.ResolveAsync(dryRun.Proposal, cancellationToken).ConfigureAwait(false);
        if (policy is null || string.IsNullOrWhiteSpace(policy.Id)
            || policy.SafeOutcomes is null || policy.RetryableOutcomes is null
            || policy.SafeOutcomes.Concat(policy.RetryableOutcomes).Any(status => !Enum.IsDefined(status))
            || policy.SafeOutcomes.Overlaps(policy.RetryableOutcomes)
            || policy.RetryableOutcomes.Any(status => status is not (ExchangeEffectStatus.Failed or ExchangeEffectStatus.Halted)))
        {
            throw new DataExchangeCommitRefusedException("commit.outcome_policy_unknown", "A known source outcome policy is required.");
        }
        var requestedAt = _clock.GetUtcNow();
        var retention = await _lifecycle.DeriveAsync(dryRun.TenantId, dryRun.RetentionClass, requestedAt, cancellationToken).ConfigureAwait(false);
        var commitRunId = new CommitRunId(Guid.NewGuid().ToString("N"));
        var batch = ExchangeIdentity.DeriveBatch(batchDerivation);
        var prepared = await PrepareWindowAsync(dryRun, current, commitRunId, batch, cancellationToken).ConfigureAwait(false);
        var priorAccessRefusals = (await _runs.ListCommitRunsAsync(dryRun.Id, cancellationToken).ConfigureAwait(false))
            .SelectMany(run => run.Effects)
            .Where(result => result.Outcome is { Status: ExchangeEffectStatus.Rejected, Code: "target.access_refused" })
            .GroupBy(result => result.EffectIdentity)
            .ToDictionary(group => group.Key, group => group.Last().Outcome);
        var completed = new ConcurrentQueue<CommitEffectResult>();
        await Parallel.ForEachAsync(
            prepared,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _bounds.MaxConcurrency,
            },
            async (effect, token) => completed.Enqueue(
                await ApplyEffectAsync(batch, policy, priorAccessRefusals, effect, token).ConfigureAwait(false)))
            .ConfigureAwait(false);
        var results = completed.OrderBy(result => result.Effect.SourceOrdinal).ToArray();

        ExchangeRunClosure.ValidateEffects(dryRun, batch, results);
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
        var census = ExchangeRunClosure.Census(results);
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
            requestedAt,
            results,
            census,
            terminalStatus,
            durableCheckpoint,
            dryRun.RetentionClass,
            retention.RetainUntil,
            retention.LegalHold);
        ExchangeRunClosure.Validate(dryRun, commitRun);
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
        DryRunRequest current,
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
                new TargetAuthorizationContext(dryRun.TenantId, current.RequestedBy, current.AuthorizationContextReference),
                payload);
            ValidatePayload(command);
            prepared.Add(new(effect, effectIdentity, attempt, command, null));
        }
        return prepared;
    }

    private async ValueTask<CommitEffectResult> ApplyEffectAsync(
        BatchIdentity batch,
        AcknowledgementPolicy policy,
        IReadOnlyDictionary<EffectIdempotencyIdentity, EffectTerminalOutcome> priorAccessRefusals,
        PreparedCommitEffect prepared,
        CancellationToken cancellationToken)
    {
        if (prepared.PrecomputedOutcome is not null)
        {
            return new(prepared.Effect, prepared.EffectIdentity, prepared.PrecomputedOutcome, false);
        }

        var recorded = await _commands.GetOutcomeAsync(prepared.EffectIdentity, cancellationToken).ConfigureAwait(false);
        if (recorded is not null)
        {
            ExchangeRunClosure.ValidateOutcome(recorded.Outcome);
            if (recorded.BatchIdentity != batch || recorded.EffectIdentity != prepared.EffectIdentity)
                throw new DataExchangeCommitRefusedException("run.ledger_mismatch", "The recorded outcome belongs to different semantic intent.");
            if (!policy.RetryableOutcomes.Contains(recorded.Outcome.Status)
                && !(recorded.Outcome.Status == ExchangeEffectStatus.Conflicted && policy.CorrectConflicts))
            {
                return new(prepared.Effect, prepared.EffectIdentity, recorded.Outcome, true);
            }
        }

        if (priorAccessRefusals.TryGetValue(prepared.EffectIdentity, out var priorRefusal))
            return new(prepared.Effect, prepared.EffectIdentity, priorRefusal, true);

        if (!await _access.CanApplyAsync(prepared.Effect, cancellationToken).ConfigureAwait(false))
        {
            return new(prepared.Effect, prepared.EffectIdentity,
                new(ExchangeEffectStatus.Rejected, "target.access_refused"), false);
        }

        var now = _clock.GetUtcNow();
        var claim = await _commands.ClaimAsync(
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
            return new(prepared.Effect, prepared.EffectIdentity,
                new(ExchangeEffectStatus.Halted, "commit.effect_in_progress"), false);
        }

        EffectTerminalOutcome outcome;
        try
        {
            outcome = recorded?.Outcome.Status == ExchangeEffectStatus.Conflicted
                ? recorded.Outcome
                : await _commands.ExecuteAndRecordAsync(prepared.Command!, cancellationToken).ConfigureAwait(false);
            ExchangeRunClosure.ValidateOutcome(outcome);
            if (outcome.Status == ExchangeEffectStatus.Conflicted && policy.CorrectConflicts)
            {
                var correction = new CanonicalForwardCorrectionCommand(prepared.Command!, outcome);
                ValidatePayload(correction);
                outcome = await _commands.CorrectAndRecordAsync(correction, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (DataExchangeCommitRefusedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            var recovered = await _commands.GetOutcomeAsync(prepared.EffectIdentity, CancellationToken.None).ConfigureAwait(false);
            outcome = recovered?.Outcome ?? new(ExchangeEffectStatus.Failed, "records.command_failed");
        }
        ExchangeRunClosure.ValidateOutcome(outcome);
        return new(prepared.Effect, prepared.EffectIdentity, outcome, false);
    }

    private void ValidatePayload<T>(T command)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(command).Length > _bounds.MaxCommandBytes)
            throw new DataExchangeCommitRefusedException("commit.command_payload_exceeded",
                "A canonical Records command exceeds the bounded payload size.");
    }

    private static bool EffectsEqual(IReadOnlyList<ProposedEffect> approved, IReadOnlyList<ProposedEffect> current)
        => approved.Count == current.Count
            && approved.OrderBy(effect => effect.SourceOrdinal).Zip(
                current.OrderBy(effect => effect.SourceOrdinal),
                (left, right) => left.SourceOrdinal == right.SourceOrdinal
                    && StringComparer.Ordinal.Equals(left.SourceRecordIdentity, right.SourceRecordIdentity)
                    && StringComparer.Ordinal.Equals(left.SourceRecordVersion, right.SourceRecordVersion)
                    && StringComparer.Ordinal.Equals(left.EffectDiscriminator, right.EffectDiscriminator)
                    && StringComparer.Ordinal.Equals(left.BoundaryAfter, right.BoundaryAfter)
                    && StringComparer.Ordinal.Equals(left.PayloadReference, right.PayloadReference)
                    && left.Metadata.Count == right.Metadata.Count
                    && left.Metadata.All(pair => right.Metadata.TryGetValue(pair.Key, out var value)
                        && StringComparer.Ordinal.Equals(pair.Value, value)))
                .All(equal => equal);

    private sealed record PreparedCommitEffect(
        ProposedEffect Effect,
        EffectIdempotencyIdentity EffectIdentity,
        AttemptId AttemptId,
        CanonicalRecordsCommand? Command,
        EffectTerminalOutcome? PrecomputedOutcome);
}
