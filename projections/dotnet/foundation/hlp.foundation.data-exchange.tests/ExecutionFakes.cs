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
    private readonly Dictionary<EffectIdempotencyIdentity, EffectLedgerEntry> _outcomes = [];
    public List<CanonicalForwardCorrectionCommand> Corrections { get; } = [];

    public bool LoseNextResponse { get; set; }

    public ValueTask<EffectLedgerEntry?> GetOutcomeAsync(EffectIdempotencyIdentity identity, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_outcomes.GetValueOrDefault(identity));

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
        Record(command.OriginalCommand, outcome);
        return ValueTask.FromResult(outcome);
    }

    private void Record(CanonicalRecordsCommand command, EffectTerminalOutcome outcome)
        => _outcomes[command.EffectIdentity] = new(command.BatchIdentity, command.EffectIdentity, command.AttemptId, outcome, DateTimeOffset.UtcNow);
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
