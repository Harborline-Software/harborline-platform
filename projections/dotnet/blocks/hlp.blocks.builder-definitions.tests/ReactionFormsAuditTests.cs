using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class ReactionFormsAuditTests
{
    [Fact]
    public async Task T616_forms_refuses_a_delegated_actor_on_submission_audit()
    {
        var instance = new EntityId("harborline", "forms", "instance-1");
        var tenant = new TenantId("tenant-a");
        var now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");
        var submission = new FormSubmissionRecord(instance, tenant, Guid.Empty, "alice", new FormDefinitionId("inspection"),
            new SemanticVersion(1, 0, 0), "fingerprint", ReadOnlyMemory<byte>.Empty, now);
        var audit = new FormMutationAuditEnvelope("audit-1", instance, tenant, "alice", ReadOnlyMemory<byte>.Empty, now);
        var commit = new FormSubmissionCommit("op-1", submission, audit, null!, null!);
        var port = new FormSubmissionKernelTransactionPort(new InMemoryFormSubmissionStore());
        await using var transaction = await port.BeginAsync();
        await transaction.StageOperationAsync(new(instance.ToString(), "op-1", "fingerprint"));
        await transaction.StageRecordAsync(commit);
        var evidence = new KernelAuditEvidence("audit-1", "alice", now, ReadOnlyMemory<byte>.Empty)
            { DelegatedActor = new("wf.follow-up", "1.0.0") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.StageAuditAsync(evidence).AsTask());
    }
}
