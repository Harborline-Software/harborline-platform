using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitReplayTests
{
    [Fact]
    public void Every_terminal_outcome_has_closed_replay_correction_acknowledgement_and_visibility_policy()
    {
        var policies = Enum.GetValues<ExchangeEffectStatus>()
            .Select(ExchangeOutcomePolicies.For)
            .ToArray();

        Assert.Equal(6, policies.Length);
        Assert.All(policies, policy => Assert.False(string.IsNullOrWhiteSpace(policy.OrdinaryReaderCode)));
        Assert.True(ExchangeOutcomePolicies.For(ExchangeEffectStatus.Applied).AcknowledgementSafe);
        Assert.True(ExchangeOutcomePolicies.For(ExchangeEffectStatus.Failed).Retryable);
        Assert.True(ExchangeOutcomePolicies.For(ExchangeEffectStatus.Conflicted).CorrectionRequired);
        Assert.False(ExchangeOutcomePolicies.For(ExchangeEffectStatus.Halted).AcknowledgementSafe);
    }

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
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System).CreateDryRunAsync(request);
        var target = new ScriptedTarget(new Dictionary<string, Queue<ExchangeEffectStatus>>
        {
            ["A"] = new([ExchangeEffectStatus.Applied]),
            ["B"] = new([ExchangeEffectStatus.Failed, ExchangeEffectStatus.Applied]),
            ["C"] = new([ExchangeEffectStatus.Applied]),
            ["D"] = new([ExchangeEffectStatus.Applied]),
        });
        var outcomes = new InMemoryEffectOutcomeStore();
        var checkpoints = new InMemoryAcquisitionCheckpointStore();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            target,
            target,
            outcomes,
            checkpoints,
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024));
        var policy = new AcknowledgementPolicy(
            new HashSet<ExchangeEffectStatus> { ExchangeEffectStatus.Applied });

        var first = await committer.CommitAsync(dryRun.Id, dryRun.Proposal, policy);
        var replay = await committer.CommitAsync(dryRun.Id, dryRun.Proposal, policy);

        Assert.Equal("cursor:a", first.DurableCheckpoint);
        Assert.Equal("cursor:d", replay.DurableCheckpoint);
        Assert.Equal(first.BatchIdentity, replay.BatchIdentity);
        Assert.NotEqual(first.Id, replay.Id);
        Assert.Equal(["A", "B", "C", "D"], target.AppliedSourceIdentities.Take(4).Order(StringComparer.Ordinal));
        Assert.Equal("B", target.AppliedSourceIdentities[4]);
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
    : ITargetAccessGate, ICanonicalRecordsCommandPort
{
    private readonly object _gate = new();
    public List<string> AppliedSourceIdentities { get; } = [];

    public ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default)
    {
        var identity = command.Effect.SourceRecordIdentity;
        ExchangeEffectStatus status;
        lock (_gate)
        {
            AppliedSourceIdentities.Add(identity);
            status = outcomes[identity].Dequeue();
        }
        return ValueTask.FromResult(new EffectTerminalOutcome(status, $"records.{status.ToString().ToLowerInvariant()}"));
    }
}
