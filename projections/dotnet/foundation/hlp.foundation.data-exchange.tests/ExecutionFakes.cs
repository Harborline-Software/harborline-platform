using Harborline.Foundation.DataExchange;

namespace Harborline.Foundation.DataExchange.Tests;

internal sealed class FakeTargetRegistry : ICanonicalTargetRegistryPort
{
    public CanonicalRecordsContract? Contract { get; set; } = new("records.customer/v1", "v1");
    public ValueTask<CanonicalRecordsContract?> ResolveAsync(string targetContract, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Contract?.Contract == targetContract ? Contract : null);
}

internal abstract class FakeTargetCommandPort : ICanonicalTargetCommandPort
{
    private readonly object _gate = new();
    private readonly Dictionary<EffectIdempotencyIdentity, EffectLedgerEntry> _outcomes = [];
    private readonly Dictionary<EffectIdempotencyIdentity, (AttemptId AttemptId, DateTimeOffset LeaseUntil)> _claims = [];
    public List<CanonicalForwardCorrectionCommand> Corrections { get; } = [];

    public bool LoseNextResponse { get; set; }

    public ValueTask<EffectLedgerEntry?> GetOutcomeAsync(EffectIdempotencyIdentity identity, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_outcomes.GetValueOrDefault(identity));
        }
    }

    public ValueTask<EffectClaim> ClaimAsync(
        BatchIdentity batchIdentity,
        EffectIdempotencyIdentity effectIdentity,
        AttemptId attemptId,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var existing = _outcomes.GetValueOrDefault(effectIdentity);
            if (existing is not null && !ExchangeOutcomePolicies.For(existing.Outcome.Status).Retryable)
                return ValueTask.FromResult(new EffectClaim(false, existing));
            if (_claims.TryGetValue(effectIdentity, out var current) && current.LeaseUntil > claimedAt)
                return ValueTask.FromResult(new EffectClaim(false, null));
            _claims[effectIdentity] = (attemptId, leaseUntil);
            return ValueTask.FromResult(new EffectClaim(true, null));
        }
    }

    public abstract ValueTask<EffectTerminalOutcome> ApplyAsync(CanonicalRecordsCommand command, CancellationToken cancellationToken = default);

    public async ValueTask<EffectTerminalOutcome> ExecuteAndRecordAsync(CanonicalRecordsCommand command, CancellationToken cancellationToken = default)
    {
        var outcome = await ApplyAsync(command, cancellationToken);
        Record(command, outcome);
        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new IOException("Response lost after target effect and outcome committed.");
        }
        return outcome;
    }

    public ValueTask<EffectTerminalOutcome> CorrectAndRecordAsync(CanonicalForwardCorrectionCommand command, CancellationToken cancellationToken = default)
    {
        Corrections.Add(command);
        var outcome = new EffectTerminalOutcome(ExchangeEffectStatus.Applied, "records.corrected");
        lock (_gate)
        {
            var existing = _outcomes.GetValueOrDefault(command.OriginalCommand.EffectIdentity);
            if (existing?.Outcome.Status != ExchangeEffectStatus.Conflicted)
                throw new ExchangeRunConflictException("Only a recorded conflict can be forward-corrected.");
            _outcomes[command.OriginalCommand.EffectIdentity] = new(
                command.OriginalCommand.BatchIdentity,
                command.OriginalCommand.EffectIdentity,
                command.OriginalCommand.AttemptId,
                outcome,
                DateTimeOffset.UtcNow);
        }
        return ValueTask.FromResult(outcome);
    }

    private void Record(CanonicalRecordsCommand command, EffectTerminalOutcome outcome)
    {
        lock (_gate)
        {
            if (!_claims.TryGetValue(command.EffectIdentity, out var claim) || claim.AttemptId != command.AttemptId)
                throw new ExchangeRunConflictException("The effect attempt no longer owns the active claim.");
            _outcomes[command.EffectIdentity] = new(command.BatchIdentity, command.EffectIdentity, command.AttemptId, outcome, DateTimeOffset.UtcNow);
            _claims.Remove(command.EffectIdentity);
        }
    }
}

internal sealed class FakeProposalEvaluator : IProposalEvaluationPort
{
    public DryRunRequest? Current { get; set; }

    public ValueTask<DryRunRequest> EvaluateAsync(DryRunArtifact approved, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Current ?? new DryRunRequest(approved.TenantId, approved.RequestedBy,
            approved.Proposal, approved.NormalizedEffects, approved.CandidateCheckpoint, approved.SnapshotReference,
            approved.AuthorizationContextReference, approved.RetentionClass));
}

internal sealed class FakeSourcePolicies : ISourceOutcomePolicyPort
{
    public static AcknowledgementPolicy Known => new("erpnext-v4",
        new HashSet<ExchangeEffectStatus> { ExchangeEffectStatus.Applied, ExchangeEffectStatus.Skipped },
        new HashSet<ExchangeEffectStatus> { ExchangeEffectStatus.Failed, ExchangeEffectStatus.Halted });

    public AcknowledgementPolicy? Policy { get; set; } = Known;

    public ValueTask<AcknowledgementPolicy?> ResolveAsync(ProposalFingerprint proposal, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Policy);
}

internal sealed class FakeLifecyclePolicy : IRunLifecyclePolicyPort
{
    public RunRetention Retention { get; set; } = new(new DateTimeOffset(2033, 9, 17, 12, 0, 0, TimeSpan.Zero), false);
    public List<(string TenantId, string RetentionClass, DateTimeOffset RequestedAt)> Calls { get; } = [];

    public ValueTask<RunRetention> DeriveAsync(string tenantId, string retentionClass,
        DateTimeOffset requestedAt, CancellationToken cancellationToken = default)
    {
        Calls.Add((tenantId, retentionClass, requestedAt));
        return ValueTask.FromResult(Retention);
    }
}
