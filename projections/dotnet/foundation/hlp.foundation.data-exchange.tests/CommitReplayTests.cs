using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitReplayTests
{
    [Fact]
    public async Task Partial_commit_advances_only_contiguous_safe_prefix_and_replay_targets_only_failure()
    {
        var runs = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest() with
        {
            Effects = [
                new(1, "A", "v1", "primary", "cursor:a", Evidence("A")),
                new(2, "B", "v1", "primary", "cursor:b", Evidence("B")),
                new(3, "C", "v1", "primary", "cursor:c", Evidence("C")),
                new(4, "D", "v1", "primary", "cursor:d", Evidence("D")),
            ],
            CandidateCheckpoint = "cursor:d",
        };
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(request);
        var target = new ScriptedTarget(new Dictionary<string, Queue<ExchangeEffectStatus>>
        {
            ["A"] = new([ExchangeEffectStatus.Applied]),
            ["B"] = new([ExchangeEffectStatus.Failed, ExchangeEffectStatus.Applied]),
            ["C"] = new([ExchangeEffectStatus.Applied]),
            ["D"] = new([ExchangeEffectStatus.Applied]),
        });
        var checkpoints = new InMemoryAcquisitionCheckpointStore();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            target,
            target,
            checkpoints,
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024), new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());

        var first = await committer.CommitAsync(dryRun.Id);
        var replay = await committer.CommitAsync(dryRun.Id);

        Assert.Equal("cursor:a", first.DurableCheckpoint);
        Assert.Equal("cursor:d", replay.DurableCheckpoint);
        Assert.Equal(first.BatchIdentity, replay.BatchIdentity);
        Assert.NotEqual(first.Id, replay.Id);
        Assert.Equal(
            ["A", "B", "C", "D", "B"],
            target.AppliedSourceIdentities);
        Assert.Equal(4, replay.Census.Applied);
        Assert.Equal(0, replay.Census.Failed);
        Assert.Equal(ExchangeRunTerminalStatus.Completed, replay.TerminalStatus);
    }

    private static IReadOnlyDictionary<string, string> Evidence(string sourceIdentity)
        => new Dictionary<string, string>
        {
            ["target"] = sourceIdentity,
            ["digest"] = $"sha256:{sourceIdentity}",
        };
}

internal sealed class ScriptedTarget(
    IReadOnlyDictionary<string, Queue<ExchangeEffectStatus>> outcomes)
    : FakeTargetCommandPort, ITargetAccessGate, ICanonicalTargetCommandPort
{
    public List<string> AppliedSourceIdentities { get; } = [];

    public ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public override ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default)
    {
        var identity = command.Effect.SourceRecordIdentity;
        AppliedSourceIdentities.Add(identity);
        var status = outcomes[identity].Dequeue();
        return ValueTask.FromResult(new EffectTerminalOutcome(status, $"records.{status.ToString().ToLowerInvariant()}"));
    }
}
