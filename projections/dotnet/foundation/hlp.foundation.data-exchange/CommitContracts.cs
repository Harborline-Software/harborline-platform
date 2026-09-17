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

public sealed record AcknowledgementPolicy(
    string Id,
    IReadOnlySet<ExchangeEffectStatus> SafeOutcomes,
    IReadOnlySet<ExchangeEffectStatus> RetryableOutcomes,
    bool CorrectConflicts = false);

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
    TargetAuthorizationContext Authorization);

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
    ValueTask<EffectTerminalOutcome> ExecuteAndRecordAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<EffectLedgerEntry?> GetOutcomeAsync(
        EffectIdempotencyIdentity identity,
        CancellationToken cancellationToken = default);
}

public interface IAcquisitionCheckpointStore
{
    ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default);

    ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string checkpoint,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryAcquisitionCheckpointStore : IAcquisitionCheckpointStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string TenantId, string DefinitionId), string> _checkpoints = [];

    public ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_checkpoints.GetValueOrDefault((tenantId, definitionId)));
        }
    }

    public ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string checkpoint,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _checkpoints[(tenantId, definitionId)] = checkpoint;
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Promotes one reviewed proposal through bounded canonical Records commands.</summary>
public sealed class DataExchangeCommitter(
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
{
    private readonly IExchangeRunStore _runs = runs ?? throw new ArgumentNullException(nameof(runs));
    private readonly IExchangeCommitAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    private readonly ITargetAccessGate _access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly ICanonicalTargetCommandPort _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    private readonly IAcquisitionCheckpointStore _checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly CommitBounds _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

    private readonly IProposalEvaluationPort _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly ISourceOutcomePolicyPort _sourcePolicies = sourcePolicies ?? throw new ArgumentNullException(nameof(sourcePolicies));

    private readonly IRunLifecyclePolicyPort _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    private readonly ICanonicalTargetRegistryPort _targets = targets ?? throw new ArgumentNullException(nameof(targets));

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
        if (batchDerivation != BatchIdentityInputs.From(current.TenantId, current.Proposal))
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
            || policy.RetryableOutcomes.Any(status => status is not (ExchangeEffectStatus.Failed or ExchangeEffectStatus.Halted)))
        {
            throw new DataExchangeCommitRefusedException("commit.outcome_policy_unknown", "A known source outcome policy is required.");
        }
        policy = policy with { SafeOutcomes = policy.SafeOutcomes.ToHashSet(), RetryableOutcomes = policy.RetryableOutcomes.ToHashSet() };
        var requestedAt = _clock.GetUtcNow();
        var retention = await _lifecycle.DeriveAsync(dryRun.TenantId, dryRun.RetentionClass, requestedAt, cancellationToken).ConfigureAwait(false);
        var commitRunId = new CommitRunId(Guid.NewGuid().ToString("N"));
        var batch = ExchangeIdentity.DeriveBatch(batchDerivation);
        var results = new List<CommitEffectResult>(dryRun.NormalizedEffects.Count);
        foreach (var effect in dryRun.NormalizedEffects.OrderBy(item => item.SourceOrdinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectIdentity = ExchangeIdentity.DeriveEffect(
                batch,
                dryRun.Proposal.TargetContract,
                effect.SourceRecordIdentity,
                effect.SourceRecordVersion,
                effect.EffectDiscriminator);
            var recorded = await _commands.GetOutcomeAsync(effectIdentity, cancellationToken).ConfigureAwait(false);
            if (recorded is not null)
            {
                ExchangeRunClosure.ValidateOutcome(recorded.Outcome);
                if (recorded.BatchIdentity != batch || recorded.EffectIdentity != effectIdentity)
                    throw new DataExchangeCommitRefusedException("run.ledger_mismatch", "The recorded outcome belongs to different semantic intent.");
            }
            if (recorded is not null && !policy.RetryableOutcomes.Contains(recorded.Outcome.Status)
                && !(recorded.Outcome.Status == ExchangeEffectStatus.Conflicted && policy.CorrectConflicts))
            {
                results.Add(new(effect, effectIdentity, recorded.Outcome, true));
                continue;
            }

            var attempt = AttemptId.New();
            var command = new CanonicalRecordsCommand(
                commitRunId,
                batch,
                effectIdentity,
                attempt,
                dryRun.Proposal.TargetContract,
                effect,
                new TargetAuthorizationContext(dryRun.TenantId, current.RequestedBy, current.AuthorizationContextReference));
            ValidatePayload(command);

            EffectTerminalOutcome outcome;
            if (!await _access.CanApplyAsync(effect, cancellationToken).ConfigureAwait(false))
            {
                outcome = new(ExchangeEffectStatus.Rejected, "target.access_refused");
            }
            else
            {
                try
                {
                    outcome = recorded?.Outcome.Status == ExchangeEffectStatus.Conflicted
                        ? recorded.Outcome
                        : await _commands.ExecuteAndRecordAsync(command, cancellationToken).ConfigureAwait(false);
                    ExchangeRunClosure.ValidateOutcome(outcome);
                    if (outcome.Status == ExchangeEffectStatus.Conflicted && policy.CorrectConflicts)
                    {
                        var correction = new CanonicalForwardCorrectionCommand(command, outcome);
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
                    outcome = new(ExchangeEffectStatus.Failed, "records.command_failed");
                }
            }
            ExchangeRunClosure.ValidateOutcome(outcome);
            results.Add(new(effect, effectIdentity, outcome, false));
        }

        ExchangeRunClosure.ValidateEffects(dryRun, batch, results);
        string? durableCheckpoint = null;
        foreach (var result in results.OrderBy(item => item.Effect.SourceOrdinal))
        {
            if (!policy.SafeOutcomes.Contains(result.Outcome.Status))
            {
                break;
            }
            durableCheckpoint = result.Effect.BoundaryAfter;
        }
        if (durableCheckpoint is not null)
        {
            await _checkpoints.PromoteAsync(
                dryRun.TenantId,
                dryRun.Proposal.DefinitionId,
                durableCheckpoint,
                cancellationToken).ConfigureAwait(false);
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
        await _runs.SaveCommitRunAsync(commitRun, cancellationToken).ConfigureAwait(false);
        return commitRun;
    }

    private void ValidatePayload<T>(T command)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(command).Length > _bounds.MaxCommandBytes)
            throw new DataExchangeCommitRefusedException("commit.command_payload_exceeded",
                "A canonical Records command exceeds the bounded payload size.");
    }
}
