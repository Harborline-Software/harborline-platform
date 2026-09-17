using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class TargetAdmissionConformanceTests
{
    [Fact]
    public async Task Caller_supplied_unregistered_target_refuses_before_any_effect()
    {
        var runs = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(request with { Proposal = request.Proposal with { TargetContract = "records.unregistered/v1" } });
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(runs, new RecordingCommitAuthority(true), target, target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => committer.CommitAsync(dryRun.Id).AsTask());
        Assert.Equal("target.contract_unregistered", refused.Code);
        Assert.Empty(target.Applied);
    }
}
