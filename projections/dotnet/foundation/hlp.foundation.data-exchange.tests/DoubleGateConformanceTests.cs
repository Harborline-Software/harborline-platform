using System.Text.Json;
using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DoubleGateConformanceTests
{
    [Fact]
    public async Task Lost_response_replays_target_owned_outcome_without_reapplying_effect()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new ContextRecordingTarget { LoseNextResponse = true };
        var committer = new DataExchangeCommitter(runs, new RecordingCommitAuthority(true), new RecordingTarget(), target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
        var first = await committer.CommitAsync(dryRun.Id);
        Assert.Equal(2, first.Census.Applied);
        Assert.Equal(ExchangeEffectStatus.Applied, (await target.GetOutcomeAsync(first.Effects[0].EffectIdentity))!.Outcome.Status);
        var replay = await committer.CommitAsync(dryRun.Id);
        Assert.Equal(2, replay.Census.Applied);
        Assert.Equal(2, target.Commands.Count);
        Assert.All(replay.Effects, result => Assert.True(result.ReplayedFromLedger));
    }

    [Fact]
    public async Task Every_target_command_receives_tenant_actor_and_authorization_context()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new ContextRecordingTarget();
        var committer = new DataExchangeCommitter(runs, new RecordingCommitAuthority(true), new RecordingTarget(), target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
        await committer.CommitAsync(dryRun.Id);
        Assert.Equal(2, target.Commands.Count);
        foreach (var command in target.Commands)
        {
            var json = JsonSerializer.SerializeToElement(command);
            Assert.True(json.TryGetProperty("Authorization", out var context));
            Assert.Equal(dryRun.TenantId, context.GetProperty("TenantId").GetString());
            Assert.Equal(dryRun.RequestedBy, context.GetProperty("ActorId").GetString());
            Assert.Equal(dryRun.AuthorizationContextReference, context.GetProperty("ContextReference").GetString());
            var stored = await target.GetOutcomeAsync(command.EffectIdentity);
            Assert.NotNull(stored);
            Assert.Equal(ExchangeEffectStatus.Applied, stored.Outcome.Status);
        }
    }
}

internal sealed class ContextRecordingTarget : FakeTargetCommandPort, ICanonicalTargetCommandPort
{
    public List<CanonicalRecordsCommand> Commands { get; } = [];
    public override ValueTask<EffectTerminalOutcome> ApplyAsync(CanonicalRecordsCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        return ValueTask.FromResult(new EffectTerminalOutcome(ExchangeEffectStatus.Applied, "records.applied"));
    }
}
