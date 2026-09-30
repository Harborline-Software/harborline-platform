using System.Collections.Concurrent;
using System.Text.Json;

namespace Harborline.Foundation.DataExchange;

/// <summary>Terminal status of one reviewed effect; ExchangeOutcomePolicies fixes how each of the six arms is retried, corrected and acknowledged.</summary>
public enum ExchangeEffectStatus
{
    /// <summary>The command was applied to the canonical target.</summary>
    Applied,
    /// <summary>No write was needed, for example a replay whose payload digest matches the ledger.</summary>
    Skipped,
    /// <summary>The target holds different content for this effect identity; needs forward correction, not retry.</summary>
    Conflicted,
    /// <summary>The effect was refused, for example by mapping or target access; needs correction and is not retryable.</summary>
    Rejected,
    /// <summary>The command failed transiently; retryable, and it never advances the checkpoint.</summary>
    Failed,
    /// <summary>The effect was left incomplete, for example another attempt holds its claim; retryable, and it never advances the checkpoint.</summary>
    Halted,
}

/// <summary>A terminal effect status with its stable machine-readable code, such as target.access_refused.</summary>
public sealed record EffectTerminalOutcome(ExchangeEffectStatus Status, string Code);

/// <summary>How readers treat one effect status: retryable, correction required, safe to acknowledge, and the code shown to ordinary readers.</summary>
public sealed record ExchangeOutcomePolicy(
    ExchangeEffectStatus Status,
    bool Retryable,
    bool CorrectionRequired,
    bool AcknowledgementSafe,
    string OrdinaryReaderCode);

/// <summary>The fixed policy table covering the six effect statuses.</summary>
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

    /// <summary>Returns the fixed policy for the status; throws KeyNotFoundException for a status value outside the six defined arms.</summary>
    public static ExchangeOutcomePolicy For(ExchangeEffectStatus status) => Policies[status];
}

/// <summary>One effect outcome recorded in the target ledger, keyed by batch and effect identity, with attempt, timestamp and optional payload digest.</summary>
public sealed record EffectLedgerEntry(
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    EffectTerminalOutcome Outcome,
    DateTimeOffset RecordedAt,
    string? PayloadDigest = null);

/// <summary>Overall result of a commit run, derived from its census.</summary>
public enum ExchangeRunTerminalStatus
{
    /// <summary>Every reviewed effect was applied.</summary>
    Completed,
    /// <summary>Nothing halted, but at least one effect was skipped, conflicted, rejected or failed.</summary>
    CompletedWithRefusals,
    /// <summary>At least one effect halted, so the run did not finish.</summary>
    Halted,
}

/// <summary>Per-status effect counts for a run; the total must account for every reviewed effect exactly once.</summary>
public sealed record ExchangeCensus(
    int Applied,
    int Skipped,
    int Conflicted,
    int Rejected,
    int Failed,
    int Halted)
{
    /// <summary>Total number of effects represented by the status counters.</summary>
    public int Accounted => Applied + Skipped + Conflicted + Rejected + Failed + Halted;
}

/// <summary>The terminal outcome of one reviewed effect, flagged when it was replayed from the ledger or a prior access refusal instead of executed now.</summary>
public sealed record CommitEffectResult(
    ProposedEffect Effect,
    EffectIdempotencyIdentity EffectIdentity,
    EffectTerminalOutcome Outcome,
    bool ReplayedFromLedger);

/// <summary>Immutable evidence of one commit run: approved dry run, batch identity, per-effect results, census, terminal status, durable checkpoint and retention.</summary>
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

/// <summary>Source-owned policy naming which outcomes may advance the checkpoint, which are retryable, and whether conflicts are forward-corrected.</summary>
public sealed record AcknowledgementPolicy
{
    /// <summary>Creates the policy; throws ArgumentException for a blank id or when a safe outcome is retryable (Failed or Halted), and ArgumentNullException for a null set.</summary>
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

    /// <summary>Stable identifier of the policy.</summary>
    public string Id { get; init; }
    /// <summary>Statuses after which the checkpoint may advance.</summary>
    public IReadOnlySet<ExchangeEffectStatus> SafeOutcomes { get; init; }
    /// <summary>Statuses a later commit re-executes; the committer accepts only Failed or Halted here.</summary>
    public IReadOnlySet<ExchangeEffectStatus> RetryableOutcomes { get; init; }
    /// <summary>When true, a Conflicted outcome is forward-corrected through the correction port instead of being reported as final.</summary>
    public bool CorrectConflicts { get; init; }
}

/// <summary>Caller options for a commit; either flag asks for rollback semantics, which are always refused as commit.rollback_refused.</summary>
public sealed record CommitOptions(bool AllOrNothingRollback = false, bool MultiCommandAtomicCommit = false);

/// <summary>Resolves source-capability-owned outcome metadata; unknown policies return null.</summary>
public interface ISourceOutcomePolicyPort
{
    /// <summary>Resolves the acknowledgement policy for the proposal's source capability; null when the policy is unknown.</summary>
    ValueTask<AcknowledgementPolicy?> ResolveAsync(ProposalFingerprint proposal, CancellationToken cancellationToken = default);
}

/// <summary>Reevaluates live semantic dependencies and effects through trusted host adapters.</summary>
public interface IProposalEvaluationPort
{
    /// <summary>Reevaluates the approved evidence against current semantic dependencies.</summary>
    ValueTask<DryRunRequest> EvaluateAsync(DryRunArtifact approved, CancellationToken cancellationToken = default);
}

/// <summary>Positive limits on one commit: effect window size, parallelism and serialized command size.</summary>
public sealed record CommitBounds
{
    /// <summary>Creates the bounds; throws ArgumentOutOfRangeException when any value is zero or negative.</summary>
    public CommitBounds(int maxWindowSize, int maxConcurrency, int maxCommandBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWindowSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommandBytes);
        MaxWindowSize = maxWindowSize;
        MaxConcurrency = maxConcurrency;
        MaxCommandBytes = maxCommandBytes;
    }

    /// <summary>Most reviewed effects one commit may contain; a larger window is refused as commit.window_exceeded.</summary>
    public int MaxWindowSize { get; }

    /// <summary>Maximum number of effects applied in parallel.</summary>
    public int MaxConcurrency { get; }

    /// <summary>Largest serialized command allowed; a larger one is refused as commit.command_payload_exceeded.</summary>
    public int MaxCommandBytes { get; }
}

/// <summary>Tenant, actor and authorization-context reference the target command port uses to authorize one write.</summary>
public sealed record TargetAuthorizationContext(string TenantId, string ActorId, string ContextReference);

/// <summary>The command sent to the canonical Records target for one effect: run, batch, effect and attempt identity, contract, payload and authorization.</summary>
public sealed record CanonicalRecordsCommand(
    CommitRunId CommitRunId,
    BatchIdentity BatchIdentity,
    EffectIdempotencyIdentity EffectIdentity,
    AttemptId AttemptId,
    string TargetContract,
    ProposedEffect Effect,
    TargetAuthorizationContext Authorization,
    CanonicalEffectPayload Payload);

/// <summary>Thrown when a commit or run-evidence check is refused; Code is the stable refusal code.</summary>
public sealed class DataExchangeCommitRefusedException(string code, string message, DryRunId? supersedingDryRunId = null) : Exception(message)
{
    /// <summary>Stable refusal code, for example run.stale or commit.forbidden.</summary>
    public string Code { get; } = code;
    /// <summary>The fresh dry run created for review when the refusal is run.stale; null for every other refusal.</summary>
    public DryRunId? SupersedingDryRunId { get; } = supersedingDryRunId;
}

/// <summary>Decides whether the caller may commit a given dry run.</summary>
public interface IExchangeCommitAuthority
{
    /// <summary>Returns true when the caller may commit the dry run; false is refused as commit.forbidden.</summary>
    ValueTask<bool> CanCommitAsync(
        DryRunArtifact dryRun,
        CancellationToken cancellationToken = default);
}

/// <summary>Per-effect target access check applied before each target write.</summary>
public interface ITargetAccessGate
{
    /// <summary>Returns true when the target permits the effect; false becomes a Rejected target.access_refused outcome.</summary>
    ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default);
}

/// <summary>A domain-owned forward correction of a conflicted canonical command, never batch undo.</summary>
public sealed record CanonicalForwardCorrectionCommand(CanonicalRecordsCommand OriginalCommand, EffectTerminalOutcome Conflict);

/// <summary>Target port that applies domain-owned forward corrections to conflicted effects.</summary>
public interface ICanonicalForwardCorrectionPort
{
    /// <summary>Applies and records a domain-owned forward correction.</summary>
    ValueTask<EffectTerminalOutcome> CorrectAndRecordAsync(CanonicalForwardCorrectionCommand command, CancellationToken cancellationToken = default);
}

/// <summary>The target command port enforces authorization, idempotency, audit, and atomic outcome recording.</summary>
public interface ICanonicalTargetCommandPort : ICanonicalForwardCorrectionPort
{
    /// <summary>Claims the effect identity for this attempt until the lease expires; Acquired is false when an outcome is already recorded (returned in ExistingOutcome) or another attempt holds the claim.</summary>
    ValueTask<EffectClaim> ClaimAsync(
        BatchIdentity batchIdentity,
        EffectIdempotencyIdentity effectIdentity,
        AttemptId attemptId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the command against the target and atomically records its terminal outcome, returning that outcome.</summary>
    ValueTask<EffectTerminalOutcome> ExecuteAndRecordAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the recorded ledger entry for an effect identity; null when nothing has been recorded.</summary>
    ValueTask<EffectLedgerEntry?> GetOutcomeAsync(
        EffectIdempotencyIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a claim: whether it was acquired and, if not, the outcome already on record.</summary>
public sealed record EffectClaim(bool Acquired, EffectLedgerEntry? ExistingOutcome);

/// <summary>Per-tenant, per-definition store of the durable source checkpoint that later runs resume from.</summary>
public interface IAcquisitionCheckpointStore
{
    /// <summary>Returns the current checkpoint for the tenant and definition, or null when none has been promoted.</summary>
    ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>Advances the checkpoint when the current one equals the expected value or the same batch is moving forward; otherwise throws CheckpointConflictException.</summary>
    ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string? expectedCurrentCheckpoint,
        string checkpoint,
        BatchIdentity batchIdentity,
        int sourceOrdinal,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when a checkpoint promotion finds the expected checkpoint stale.</summary>
public sealed class CheckpointConflictException(string message) : Exception(message)
{
    /// <summary>Stable refusal code emitted when the expected checkpoint is stale.</summary>
    public string Code => "checkpoint.conflict";
}
/// <summary>Thread-safe in-memory checkpoint store for tests and single-process hosts; state is lost on exit.</summary>
public sealed class InMemoryAcquisitionCheckpointStore : IAcquisitionCheckpointStore
{
    /// <summary>Guards the checkpoint dictionary.</summary>
    private readonly object _gate = new();
    private readonly Dictionary<(string TenantId, string DefinitionId), CheckpointState> _checkpoints = [];

    /// <summary>Reads the current checkpoint for the tenant and definition.</summary>
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

    /// <summary>Advances a checkpoint only when its expected predecessor still matches.</summary>
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

    /// <summary>Stored checkpoint value with the batch and source ordinal that produced it.</summary>
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
    /// <summary>Store for dry-run and commit-run evidence.</summary>
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    /// <summary>Decides whether the caller may commit a dry run.</summary>
    private readonly IExchangeCommitAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    /// <summary>Per-effect target access check.</summary>
    private readonly ITargetAccessGate _access = access ?? throw new ArgumentNullException(nameof(access));
    /// <summary>Target command port that claims, executes and records effects.</summary>
    private readonly ICanonicalTargetCommandPort _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    /// <summary>Durable source checkpoint store.</summary>
    private readonly IAcquisitionCheckpointStore _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    /// <summary>Protected store holding canonical effect payloads by reference.</summary>
    private readonly IProtectedEffectPayloadStore _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
    /// <summary>Time source for request, claim and record timestamps.</summary>
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    /// <summary>Window, concurrency and command-size limits.</summary>
    private readonly CommitBounds _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

    /// <summary>Re-evaluates the approved proposal against live inputs.</summary>
    private readonly IProposalEvaluationPort _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    /// <summary>Resolves the source's acknowledgement policy.</summary>
    private readonly ISourceOutcomePolicyPort _sourcePolicies = sourcePolicies ?? throw new ArgumentNullException(nameof(sourcePolicies));

    /// <summary>Derives retention and legal hold for a run.</summary>
    private readonly IRunLifecyclePolicyPort _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    /// <summary>Resolves the canonical Records contract for the target.</summary>
    private readonly ICanonicalTargetRegistryPort _targets = targets ?? throw new ArgumentNullException(nameof(targets));

    /// <summary>Creates a committer backed by a private in-memory payload store; use the primary constructor to share one.</summary>
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

    /// <summary>Commits an approved dry run through per-effect audited target commands; every refusal is a DataExchangeCommitRefusedException with a stable code (run.stale, commit.forbidden, commit.window_exceeded, target.contract_unregistered, commit.outcome_policy_unknown) and nothing is partially rolled back.</summary>
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
