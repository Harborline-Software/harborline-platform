using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitBoundsAndFailureTests
{
    [Theory]
    [InlineData(0, 1, 1024)]
    [InlineData(1, 0, 1024)]
    [InlineData(1, 1, 0)]
    public void Commit_bounds_must_be_positive(int window, int concurrency, int commandBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommitBounds(window, concurrency, commandBytes));
    }

    [Fact]
    public async Task Oversized_canonical_command_is_refused_before_target_dispatch()
    {
        var runs = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest() with
        {
            Effects =
            [
                Fixtures.DryRunRequest().Effects[0] with
                {
                    Metadata = new Dictionary<string, string> { ["digest"] = new string('a', 2048) },
                },
            ],
        };
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(request);
        var target = new RecordingTarget();
        var committer = CreateCommitter(runs, target, target, new CommitBounds(10, 1, 256));

        var exception = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(
            () => committer.CommitAsync(dryRun.Id).AsTask());

        Assert.Equal("commit.command_payload_exceeded", exception.Code);
        Assert.Empty(target.Applied);
    }

    [Fact]
    public async Task Target_failure_becomes_a_closed_failed_outcome_and_does_not_abort_later_effects()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new ThrowOnceTarget();
        var committer = CreateCommitter(runs, target, target, new CommitBounds(10, 1, 64 * 1024));

        var commit = await committer.CommitAsync(dryRun.Id);

        Assert.Equal(2, commit.Census.Accounted);
        Assert.Equal(1, commit.Census.Failed);
        Assert.Equal(1, commit.Census.Applied);
        Assert.Equal("records.command_failed", commit.Effects[0].Outcome.Code);
        Assert.Equal(ExchangeRunTerminalStatus.CompletedWithRefusals, commit.TerminalStatus);
        Assert.Equal(2, target.Calls);
    }

    [Fact]
    public async Task Commit_evidence_preserves_the_structured_batch_derivation_record()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new RecordingTarget();
        var committer = CreateCommitter(runs, target, target, new CommitBounds(10, 1, 64 * 1024));

        var commit = await committer.CommitAsync(dryRun.Id);

        Assert.Equal(dryRun.TenantId, commit.BatchDerivation.TenantId);
        Assert.Equal(dryRun.Proposal.MappingDigest, commit.BatchDerivation.MappingDigest);
        Assert.Equal(dryRun.Proposal.InputBoundary, commit.BatchDerivation.InputBoundary);
        Assert.Equal(commit.BatchIdentity, ExchangeIdentity.DeriveBatch(commit.BatchDerivation));
    }

    private static DataExchangeCommitter CreateCommitter(
        IExchangeRunStore runs,
        ITargetAccessGate access,
        ICanonicalTargetCommandPort target,
        CommitBounds bounds)
        => new(
            runs,
            new RecordingCommitAuthority(true),
            access,
            target,
            new InMemoryAcquisitionCheckpointStore(),
            TimeProvider.System,
            bounds, new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
}

internal sealed class ThrowOnceTarget : FakeTargetCommandPort, ITargetAccessGate, ICanonicalTargetCommandPort
{
    public int Calls { get; private set; }

    public ValueTask<bool> CanApplyAsync(ProposedEffect effect, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public override ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Calls == 1)
        {
            throw new InvalidOperationException("target unavailable");
        }

        return ValueTask.FromResult(new EffectTerminalOutcome(ExchangeEffectStatus.Applied, "records.applied"));
    }
}
